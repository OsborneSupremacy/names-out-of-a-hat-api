namespace GiftExchange.Library.Services;

/// <summary>
/// Service for moderating user-generated content using AWS Comprehend
/// </summary>
[UsedImplicitly]
internal class ContentModerationService : IContentModerationService
{
    /// <summary>
    /// The largest a single text segment may be. Comprehend's own limit is 1 KB per segment, and
    /// this sits under it so that a segment landing on the boundary is not rejected over one byte.
    /// </summary>
    private const int MaxSegmentBytes = 1000;

    /// <summary>
    /// The most segments Comprehend accepts in one request. Its other limit, 10 KB across the
    /// whole list, cannot bind before this one does while <see cref="MaxSegmentBytes"/> is 1000.
    /// </summary>
    private const int MaxSegmentsPerRequest = 10;

    private readonly IAmazonComprehend _comprehendClient;

    private readonly ILogger<ContentModerationService> _logger;

    // Threshold for toxicity detection (0.0 to 1.0)
    // 0.5 is recommended by AWS for balanced detection
    private readonly float _toxicityThreshold;

    public ContentModerationService(
        IAmazonComprehend comprehendClient,
        ILogger<ContentModerationService> logger)
    {
        _comprehendClient = comprehendClient ?? throw new ArgumentNullException(nameof(comprehendClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _toxicityThreshold = (float)EnvReader.GetDoubleValue("CONTENT_MODERATION_THRESHOLD");
    }

    /// <summary>
    /// Validates that the provided text does not contain toxic or inappropriate content.
    /// </summary>
    /// <remarks>
    /// The message-shaped form of <see cref="ModerateAsync"/>, for the callers that report problems
    /// as a list of error strings. Fails closed, as that does.
    /// </remarks>
    /// <param name="text">The text to validate</param>
    /// <param name="fieldName">The name of the field being validated (for error messages)</param>
    /// <returns>A tuple indicating if validation passed and an error message if it failed</returns>
    public async Task<(bool IsValid, string ErrorMessage)> ValidateContentAsync(string text, string fieldName) =>
        await ModerateAsync(text, fieldName).ConfigureAwait(false) switch
        {
            ModerationVerdict.Clean => (true, string.Empty),

            ModerationVerdict.Toxic =>
                (false, $"The {fieldName} contains inappropriate content and cannot be accepted."),

            // Deliberately distinct from the rejection message above: the caller's content may be
            // perfectly fine, and telling someone their name is inappropriate when the checker was
            // simply unreachable is both wrong and unhelpful.
            _ => (false, $"We couldn't check the {fieldName} just now. Please try again in a moment.")
        };

    /// <summary>
    /// Checks text for toxic content, and says whether it was refused or could not be checked.
    /// </summary>
    /// <remarks>
    /// Fails closed: if the check cannot be performed, the answer is
    /// <see cref="ModerationVerdict.Unavailable"/>, never <see cref="ModerationVerdict.Clean"/>.
    /// Empty text is the one exception, since there is nothing to check.
    ///
    /// Text longer than Comprehend will take in one segment is split and sent as several, across
    /// as many requests as it takes. Before that, anything over 1 KB was sent as a single segment
    /// and came back as a <c>TextSizeLimitExceededException</c>, which the fail-closed catch below
    /// turned into "we couldn't check it, try again in a moment" -- advice that could never come
    /// true, however many times the organizer retried. A hat's additional information may be 2,000
    /// characters, so that was reachable from the edit screen.
    /// </remarks>
    /// <param name="fieldName">What the text is, for the log line.</param>
    public async Task<ModerationVerdict> ModerateAsync(string text, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ModerationVerdict.Clean;

        try
        {
            var toxicLabels = new List<ToxicContent>();

            // Batched rather than capped, so that how long a caller's text may be stays the
            // caller's decision instead of being dictated by Comprehend's request shape.
            foreach (var batch in SplitIntoSegments(text).Chunk(MaxSegmentsPerRequest))
            {
                var request = new DetectToxicContentRequest
                {
                    LanguageCode = LanguageCode.En,
                    TextSegments = [.. batch.Select(segment => new TextSegment { Text = segment })]
                };

                var response = await _comprehendClient.DetectToxicContentAsync(request);

                if (response.ResultList is null)
                    continue;

                // Every segment is scored separately, and one bad passage is enough to reject the
                // whole field, so all of them are gathered rather than only the first.
                toxicLabels.AddRange(response.ResultList
                    .Where(result => result.Labels is not null)
                    .SelectMany(result => result.Labels)
                    .Where(label => label.Score >= _toxicityThreshold));
            }

            if (toxicLabels.Count <= 0)
                return ModerationVerdict.Clean;

            _logger.LogWarning(
                "Content moderation flagged {FieldName} with toxic content. Labels: {Labels}, Scores: {Scores}",
                fieldName,
                string.Join(", ", toxicLabels.Select(l => l.Name)),
                string.Join(", ", toxicLabels.Select(l => l.Score))
            );

            return ModerationVerdict.Toxic;
        }
        catch (Exception ex)
        {
            // Fail closed. Everything moderated here ends up in an email sent from our SES
            // identity, so accepting unchecked content during a Comprehend outage would put that
            // reputation in someone else's hands. The AWS SDK has already exhausted its own
            // retries by the time an exception reaches us, so there is nothing left to wait for.
            _logger.LogError(ex, "Content moderation failed for {FieldName}; rejecting the request.", fieldName);

            return ModerationVerdict.Unavailable;
        }
    }

    /// <summary>
    /// Breaks text into segments that each fit inside Comprehend's per-segment limit.
    /// </summary>
    /// <remarks>
    /// The limit is on UTF-8 bytes rather than characters, which is why this counts bytes as it
    /// goes. An emoji costs four of them, and a gift exchange is one of the places people reach for
    /// emoji, so a character count would happily pass a string well under 1,000 characters that was
    /// well over 1,000 bytes -- the exact failure this splitting exists to prevent.
    ///
    /// Segments end at whitespace wherever there is any to end at, so a sentence is usually scored
    /// whole. A run with no whitespace in it -- a long URL being the realistic case -- is cut at the
    /// last character that fits. Toxicity is scored per segment, so where the cuts land can move a
    /// score a little; that is accepted, because the alternative was not scoring the text at all.
    /// </remarks>
    internal static List<string> SplitIntoSegments(string text)
    {
        var segments = new List<string>();
        var current = new StringBuilder();
        var currentBytes = 0;

        // Where the last whitespace fell within current, and what current weighed at that point.
        var breakAt = 0;
        var bytesAtBreak = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;

            if (currentBytes + runeBytes > MaxSegmentBytes && current.Length > 0)
            {
                if (breakAt > 0)
                {
                    Emit(segments, current.ToString(0, breakAt));

                    // What followed the last whitespace is not lost, it opens the next segment.
                    var carried = current.ToString(breakAt, current.Length - breakAt);
                    current.Clear().Append(carried);
                    currentBytes -= bytesAtBreak;
                }
                else
                {
                    Emit(segments, current.ToString());
                    current.Clear();
                    currentBytes = 0;
                }

                breakAt = 0;
                bytesAtBreak = 0;
            }

            current.Append(rune.ToString());
            currentBytes += runeBytes;

            if (!Rune.IsWhiteSpace(rune)) continue;

            breakAt = current.Length;
            bytesAtBreak = currentBytes;
        }

        Emit(segments, current.ToString());

        return segments;

        // Comprehend has nothing to say about whitespace, and a segment of it would only spend a
        // slot in the request.
        static void Emit(List<string> into, string segment)
        {
            if (!string.IsNullOrWhiteSpace(segment))
                into.Add(segment.Trim());
        }
    }

    /// <summary>
    /// Validates multiple text fields at once
    /// </summary>
    /// <param name="fieldsToValidate">Dictionary of field names to their text values</param>
    /// <returns>A tuple indicating if all validations passed and a list of error messages</returns>
    public async Task<(bool IsValid, List<string> ErrorMessages)> ValidateMultipleFieldsAsync(
        Dictionary<string, string> fieldsToValidate)
    {
        var errorMessages = new List<string>();

        foreach (var (fieldName, text) in fieldsToValidate)
        {
            var (isValid, errorMessage) = await ValidateContentAsync(text, fieldName);
            if (!isValid && errorMessage.Any())
                errorMessages.Add(errorMessage);
        }

        return (errorMessages.Count == 0, errorMessages);
    }
}
