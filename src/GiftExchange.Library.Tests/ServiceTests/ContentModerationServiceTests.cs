using System.Text;
using Amazon.Comprehend;
using Microsoft.Extensions.Logging;
using Amazon.Comprehend.Model;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// Everything this service approves ends up in an email sent from our SES identity, so it fails
/// closed. These tests exist mostly to stop that quietly reverting.
/// </summary>
public class ContentModerationServiceTests
{
    private readonly IAmazonComprehend _comprehend = Substitute.For<IAmazonComprehend>();

    private readonly IContentModerationService _sut;

    public ContentModerationServiceTests()
    {
        DotEnv.Load();
        _sut = new ContentModerationService(_comprehend, Substitute.For<ILogger<ContentModerationService>>());
    }

    [Fact]
    public async Task ValidateContentAsync_GivenCleanContent_IsValid()
    {
        // arrange
        RespondWithScore(0.01f);

        // act
        var (isValid, error) = await _sut.ValidateContentAsync("Family Christmas", "gift exchange name");

        // assert
        isValid.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateContentAsync_GivenContentOverTheThreshold_IsRejected()
    {
        // arrange
        RespondWithScore(0.99f);

        // act
        var (isValid, error) = await _sut.ValidateContentAsync("something nasty", "gift exchange name");

        // assert
        isValid.Should().BeFalse();
        error.Should().Contain("inappropriate content");
    }

    [Fact]
    public async Task ValidateContentAsync_WhenComprehendThrows_RejectsRatherThanAccepting()
    {
        // arrange
        _comprehend
            .DetectToxicContentAsync(Arg.Any<DetectToxicContentRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonComprehendException("Comprehend is unavailable"));

        // act
        var (isValid, error) = await _sut.ValidateContentAsync("Family Christmas", "gift exchange name");

        // assert: unchecked content must not reach an email sent from our SES identity.
        isValid.Should().BeFalse();

        // The content may be perfectly fine, so the message has to read as "try again" rather than
        // accusing the user of writing something offensive.
        error.Should().NotContain("inappropriate");
        error.Should().Contain("try again");
    }

    [Fact]
    public async Task ModerateAsync_GivenCleanContent_IsClean()
    {
        // arrange
        RespondWithScore(0.01f);

        // act
        var verdict = await _sut.ModerateAsync("A cast iron skillet", "gift ideas");

        // assert
        verdict.Should().Be(ModerationVerdict.Clean);
    }

    [Fact]
    public async Task ModerateAsync_GivenContentOverTheThreshold_IsToxic()
    {
        // arrange
        RespondWithScore(0.99f);

        // act
        var verdict = await _sut.ModerateAsync("something nasty", "gift ideas");

        // assert
        verdict.Should().Be(ModerationVerdict.Toxic);
    }

    [Fact]
    public async Task ModerateAsync_WhenComprehendThrows_IsUnavailableRatherThanClean()
    {
        // arrange
        _comprehend
            .DetectToxicContentAsync(Arg.Any<DetectToxicContentRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonComprehendException("Comprehend is unavailable"));

        // act
        var verdict = await _sut.ModerateAsync("A cast iron skillet", "gift ideas");

        // assert: fails closed, and says why, so the caller can tell its user to try again rather
        // than to reword something that was never checked.
        verdict.Should().Be(ModerationVerdict.Unavailable);
    }

    [Fact]
    public async Task ModerateAsync_GivenEmptyText_IsCleanWithoutCallingComprehend()
    {
        // act
        var verdict = await _sut.ModerateAsync("  ", "gift ideas");

        // assert
        verdict.Should().Be(ModerationVerdict.Clean);
        await _comprehend.DidNotReceive()
            .DetectToxicContentAsync(Arg.Any<DetectToxicContentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateMultipleFieldsAsync_WhenComprehendThrows_RejectsEveryField()
    {
        // arrange
        _comprehend
            .DetectToxicContentAsync(Arg.Any<DetectToxicContentRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonComprehendException("Comprehend is unavailable"));

        // act
        var (isValid, errors) = await _sut.ValidateMultipleFieldsAsync(new Dictionary<string, string>
        {
            ["gift exchange name"] = "Family Christmas",
            ["organizer name"] = "Ben"
        });

        // assert
        isValid.Should().BeFalse();
        errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task ValidateContentAsync_GivenEmptyText_IsValidWithoutCallingComprehend()
    {
        // act: optional fields arrive empty, and there is nothing to check.
        var (isValid, _) = await _sut.ValidateContentAsync(string.Empty, "price range");

        // assert
        isValid.Should().BeTrue();
        await _comprehend
            .DidNotReceive()
            .DetectToxicContentAsync(Arg.Any<DetectToxicContentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateContentAsync_GivenTextOverOneKilobyte_StillChecksIt()
    {
        // arrange: additional information may be 2,000 characters, and Comprehend takes 1 KB in a
        // segment. This used to reach the service as one oversized segment and fail closed.
        RespondWithScore(0.01f);
        var text = string.Join(" ", Enumerable.Repeat("gift", 400));

        // act
        var (isValid, error) = await _sut.ValidateContentAsync(text, "additional information");

        // assert
        isValid.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateContentAsync_GivenTextOverOneKilobyte_SendsSegmentsWithinComprehendsLimits()
    {
        // arrange
        RespondWithScore(0.01f);

        // act
        await _sut.ValidateContentAsync(string.Join(" ", Enumerable.Repeat("gift", 400)), "additional information");

        // assert
        var requests = _comprehend
            .ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<DetectToxicContentRequest>()
            .ToList();

        requests.Should().NotBeEmpty();
        requests.Should().AllSatisfy(request =>
        {
            request.TextSegments.Should().HaveCountLessThanOrEqualTo(10);
            request.TextSegments.Should()
                .AllSatisfy(segment => Encoding.UTF8.GetByteCount(segment.Text).Should().BeLessThanOrEqualTo(1024));
            Encoding.UTF8
                .GetByteCount(string.Concat(request.TextSegments.Select(segment => segment.Text)))
                .Should()
                .BeLessThanOrEqualTo(10 * 1024);
        });
    }

    [Fact]
    public async Task ValidateContentAsync_GivenToxicContentAfterTheFirstSegment_IsRejected()
    {
        // arrange: scoring is per segment, so a service reading only the first result would let
        // anything past the opening kilobyte through.
        _comprehend
            .DetectToxicContentAsync(Arg.Any<DetectToxicContentRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new DetectToxicContentResponse
            {
                ResultList =
                [
                    .. callInfo.Arg<DetectToxicContentRequest>().TextSegments
                        .Select((_, index) => new ToxicLabels
                        {
                            Labels =
                            [
                                new ToxicContent
                                {
                                    Name = ToxicContentType.PROFANITY,
                                    Score = index == 0 ? 0.01f : 0.99f
                                }
                            ]
                        })
                ]
            });

        // act
        var (isValid, error) = await _sut.ValidateContentAsync(
            string.Join(" ", Enumerable.Repeat("gift", 400)),
            "additional information");

        // assert
        isValid.Should().BeFalse();
        error.Should().Contain("inappropriate content");
    }

    [Fact]
    public async Task ValidateContentAsync_GivenMoreSegmentsThanOneRequestTakes_SendsSeveralRequests()
    {
        // arrange: 10 segments per request is Comprehend's limit, and how much text a caller may
        // submit should not be decided by it.
        RespondWithScore(0.01f);

        // act
        await _sut.ValidateContentAsync(string.Join(" ", Enumerable.Repeat("gift", 4000)), "gift ideas");

        // assert
        _comprehend
            .ReceivedCalls()
            .Count(call => call.GetArguments()[0] is DetectToxicContentRequest)
            .Should()
            .BeGreaterThan(1);
    }

    [Fact]
    public void SplitIntoSegments_GivenMultiByteCharacters_MeasuresBytesNotCharacters()
    {
        // arrange: an emoji is one character but four UTF-8 bytes. Counting characters would put
        // roughly four times the limit into a segment.
        var text = string.Join(" ", Enumerable.Repeat("🎁", 2000));

        // act
        var segments = ContentModerationService.SplitIntoSegments(text);

        // assert
        segments.Should()
            .AllSatisfy(segment => Encoding.UTF8.GetByteCount(segment).Should().BeLessThanOrEqualTo(1000));
    }

    [Fact]
    public void SplitIntoSegments_GivenAnUnbrokenRunLongerThanASegment_CutsItRatherThanOverflowing()
    {
        // arrange: a wishlist URL is the realistic way to get a very long run with no whitespace.
        var text = $"https://example.com/{new string('a', 3000)}";

        // act
        var segments = ContentModerationService.SplitIntoSegments(text);

        // assert
        segments.Should().HaveCountGreaterThan(1);
        segments.Should()
            .AllSatisfy(segment => Encoding.UTF8.GetByteCount(segment).Should().BeLessThanOrEqualTo(1000));
    }

    [Fact]
    public void SplitIntoSegments_LosesNoText()
    {
        // arrange
        const string text = "A cast iron skillet, a book about bread, and a scarf in any colour but beige.";

        // act
        var segments = ContentModerationService.SplitIntoSegments(text);

        // assert: segments are trimmed at the joins, so the words are what is compared.
        string.Join(" ", segments).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should()
            .BeEquivalentTo(text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void SplitIntoSegments_GivenTextThatFitsInOneSegment_ReturnsItWhole()
    {
        // act
        var segments = ContentModerationService.SplitIntoSegments("Family Christmas");

        // assert
        segments.Should().ContainSingle().Which.Should().Be("Family Christmas");
    }

    private void RespondWithScore(float score) =>
        _comprehend
            .DetectToxicContentAsync(Arg.Any<DetectToxicContentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DetectToxicContentResponse
            {
                ResultList =
                [
                    new ToxicLabels
                    {
                        Labels = [new ToxicContent { Name = ToxicContentType.PROFANITY, Score = score }]
                    }
                ]
            });
}
