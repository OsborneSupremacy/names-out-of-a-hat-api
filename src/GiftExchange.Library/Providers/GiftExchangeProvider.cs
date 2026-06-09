using Amazon.DynamoDBv2.Model;

namespace GiftExchange.Library.Providers;

[UsedImplicitly]
public class GiftExchangeProvider
{
    private readonly IAmazonDynamoDB _dynamoDbClient;

    private readonly ILogger<GiftExchangeProvider> _logger;

    private readonly string _tableName;

    // ReSharper disable once ConvertToPrimaryConstructor
    public GiftExchangeProvider(
        IAmazonDynamoDB dynamoDbClient,
        ILogger<GiftExchangeProvider> logger
        )
    {
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tableName = EnvReader.GetStringValue("TABLE_NAME");
    }

    public async Task<ImmutableList<HatMetaData>> GetHatsAsync(string organizerEmail)
    {
        var request = new QueryRequest
        {
            TableName = _tableName,
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :skPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new() { S = $"ORGANIZER#{organizerEmail}#HAT" },
                [":skPrefix"] = new() { S = "HAT#" }
            },
            ConsistentRead = true
        };

        var response = await _dynamoDbClient.QueryAsync(request)
            .ConfigureAwait(false);

        if (response.Items is not { Count: > 0 })
            return [];

        return response.Items
            .Select(i => new HatMetaData
            {
                HatId = Guid.Parse(i["HatId"].S),
                HatName = i["HatName"].S,
                Status = i.TryGetValue("HatStatus", out var status) ? status.S : HatStatus.InProgress
            })
            .ToImmutableList();
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="hatDataModel"></param>
    /// <returns>true if hat was created, false is had already existed.</returns>
    public async Task<bool> CreateHatAsync(HatDataModel hatDataModel)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new() { S = $"ORGANIZER#{hatDataModel.OrganizerEmail}#HAT" },
            ["SK"] = new() { S = $"HAT#{hatDataModel.HatId}" },
            ["HatId"] = new() { S = hatDataModel.HatId.ToString() },
            ["OrganizerName"] = new() { S = hatDataModel.OrganizerName },
            ["HatName"] = new() { S = hatDataModel.HatName },
            ["HatStatus"] = new() { S = hatDataModel.Status },
            ["AdditionalInformation"] = new() { S = hatDataModel.AdditionalInformation },
            ["PriceRange"] = new() { S = hatDataModel.PriceRange },
            ["InvitationsQueuedDate"] = new() { S = DateTimeOffset.MinValue.ToString("o") }
        };

        var request = new PutItemRequest
        {
            TableName = _tableName,
            Item = item,
            ConditionExpression = "attribute_not_exists(PK) AND attribute_not_exists(SK)"
        };

        try
        {
            await _dynamoDbClient.PutItemAsync(request)
                .ConfigureAwait(false);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<(bool exists, Guid hatId)> DoesHatAlreadyExistAsync(
        string organizerEmail,
        string hatName
    )
    {
        var hats = await GetHatsAsync(organizerEmail)
            .ConfigureAwait(false);

        if(!hats.Any())
            return (false, Guid.Empty);

        return hats.FirstOrDefault(h => h.HatName.ContentEquals(hatName)) is { } hat
            ? (true, hat.HatId)
            : (false, Guid.Empty);
    }

    public async Task<(bool exists, Hat hat)> GetHatAsync(string organizerEmail, Guid hatId)
    {
        if (string.IsNullOrWhiteSpace(organizerEmail) || hatId == Guid.Empty)
            return (false, Hats.Empty);

        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{organizerEmail}#HAT" },
                ["SK"] = new() { S = $"HAT#{hatId}" }
            }
        };

        var response = await _dynamoDbClient
            .GetItemAsync(request)
            .ConfigureAwait(false);

        if (response.Item == null || response.Item.Count == 0)
            return (false, Hats.Empty);

        var participants = await GetParticipantsAsync(organizerEmail, hatId)
            .ConfigureAwait(false);

        var hat = new Hat
        {
            Id = Guid.Parse(response.Item["HatId"].S),
            Name = response.Item["HatName"].S,
            Status = response.Item.TryGetValue("HatStatus", out var status) ? status.S : HatStatus.InProgress,
            AdditionalInformation = response.Item["AdditionalInformation"].S,
            PriceRange = response.Item["PriceRange"].S,
            Organizer = new Person {
                Name = response.Item["OrganizerName"].S,
                Email = organizerEmail
            },
            Participants = participants,
            InvitationsQueuedDate = response.Item.TryGetValue("InvitationsQueuedDate", out var iqd)
                ? DateTimeOffset.Parse(iqd.S)
                : DateTimeOffset.MinValue
        };
        return (true, hat);
    }

    public async Task EditHatAsync(EditHatRequest request)
    {
        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{request.OrganizerEmail}#HAT" },
                ["SK"] = new() { S = $"HAT#{request.HatId}" }
            },
            UpdateExpression = "SET HatName = :name, AdditionalInformation = :additionalInfo, PriceRange = :priceRange",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":name"] = new() { S = request.Name },
                [":additionalInfo"] = new() { S = request.AdditionalInformation },
                [":priceRange"] = new() { S = request.PriceRange }
            }
        };

        await _dynamoDbClient.UpdateItemAsync(updateRequest).ConfigureAwait(false);
    }

    public async Task DeleteHatAsync(DeleteHatRequest request)
    {
        List<Task> deleteTasks = [];

        var deleteHatRequest = new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{request.OrganizerEmail}#HAT" },
                ["SK"] = new() { S = $"HAT#{request.HatId}" }
            }
        };

        deleteTasks.Add(_dynamoDbClient.DeleteItemAsync(deleteHatRequest));

        var participants = await GetParticipantsAsync(request.OrganizerEmail, request.HatId)
            .ConfigureAwait(false);

        // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
        // No. It's more readable this way.
        foreach (var participant in participants)
        {
            var deleteParticipantRequest = new DeleteItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new() { S = $"ORGANIZER#{request.OrganizerEmail}#HAT#{request.HatId}#PARTICIPANT" },
                    ["SK"] = new() { S = $"PARTICIPANT#{participant.Person.Email}" }
                }
            };

            deleteTasks.Add(_dynamoDbClient .DeleteItemAsync(deleteParticipantRequest) );
        }

        await Task.WhenAll(deleteTasks).ConfigureAwait(false);
    }

    public async Task UpdateHatStatusAsync(
        string organizerEmail,
        Guid hatId,
        string newStatus
        )
    {
        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{organizerEmail}#HAT" },
                ["SK"] = new() { S = $"HAT#{hatId}" }
            },
            UpdateExpression = "SET HatStatus = :status",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = new() { S = newStatus }
            }
        };

        await _dynamoDbClient
            .UpdateItemAsync(updateRequest)
            .ConfigureAwait(false);
    }

    public async Task<Participant> CreateParticipantAsync(
        AddParticipantRequest request,
        ImmutableList<Participant> existingParticipants
        )
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new() { S = $"ORGANIZER#{request.OrganizerEmail}#HAT#{request.HatId}#PARTICIPANT" },
            ["SK"] = new() { S = $"PARTICIPANT#{request.Email}" },
            ["Name"] = new() { S = request.Name },
            ["Email"] = new() { S = request.Email }
        };

        var eligibleParticipantNames = existingParticipants
            .Select(p => p.Person.Name)
            .ToList();

        if(existingParticipants.Any())
            item.Add("EligibleParticipants", new() { SS = eligibleParticipantNames });

        var putItemRequest = new PutItemRequest
        {
            TableName = _tableName,
            Item = item,
            ConditionExpression = "attribute_not_exists(PK) AND attribute_not_exists(SK)"
        };

        await _dynamoDbClient.PutItemAsync(putItemRequest)
            .ConfigureAwait(false);

        return new Participant
        {
            PickedRecipient = string.Empty,
            Person = new Person
            {
                Name = request.Name,
                Email =  request.Email
            },
            EligibleRecipients = eligibleParticipantNames.ToImmutableList()
        };
    }

    public async Task<ImmutableList<Participant>> GetParticipantsAsync(
        string organizerEmail,
        Guid hatId
        )
    {
        var request = new QueryRequest
        {
            TableName = _tableName,
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :skPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new() { S = $"ORGANIZER#{organizerEmail}#HAT#{hatId}#PARTICIPANT" },
                [":skPrefix"] = new() { S = "PARTICIPANT#" }
            },
            ConsistentRead = true
        };

        var response = await _dynamoDbClient
            .QueryAsync(request)
            .ConfigureAwait(false);

        if (response.Items is not { Count: > 0 })
            return [];

        return response.Items
            .Select(i => new Participant
            {
                PickedRecipient = i.TryGetValue("PickedRecipient", out var pr) ? pr.S : string.Empty,
                Person = new Person
                {
                    Email = i["Email"].S,
                    Name = i["Name"].S
                },
                EligibleRecipients = i.TryGetValue("EligibleParticipants", out var er)
                    ? er.SS.ToImmutableList()
                    : []
            })
            .ToImmutableList();
    }

    public async Task<(bool participantExists, Participant participant)> GetParticipantAsync(
        string requestOrganizerEmail,
        Guid requestHatId,
        string requestParticipantEmail
        )
    {
        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{requestOrganizerEmail}#HAT#{requestHatId}#PARTICIPANT" },
                ["SK"] = new() { S = $"PARTICIPANT#{requestParticipantEmail}" }
            }
        };

        var response = await _dynamoDbClient
            .GetItemAsync(request)
            .ConfigureAwait(false);

        if (response.Item == null || response.Item.Count == 0)
            return (false, Participants.Empty);

        var participant = new Participant
        {
            PickedRecipient = response.Item.TryGetValue("PickedRecipient", out var pr) ? pr.S : string.Empty,
            Person = new Person
            {
                Email = response.Item["Email"].S,
                Name = response.Item["Name"].S
            },
            EligibleRecipients = response.Item.TryGetValue("EligibleParticipants", out var er)
                ? er.SS.ToImmutableList()
                : []
        };

        return (true, participant);
    }

    public async Task AddParticipantEligibleRecipientAsync(
        string organizerEmail,
        Guid hatId,
        string participantEmail,
        string recipientName
        )
    {
        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{organizerEmail}#HAT#{hatId}#PARTICIPANT" },
                ["SK"] = new() { S = $"PARTICIPANT#{participantEmail}" }
            },
            UpdateExpression = "ADD EligibleParticipants :newRecipient",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":newRecipient"] = new() { SS = [recipientName] }
            }
        };

        await _dynamoDbClient
            .UpdateItemAsync(updateRequest)
            .ConfigureAwait(false);
    }

    public async Task UpdateEligibleRecipientsAsync(
        string organizerEmail,
        Guid hatId,
        string participantEmail,
        ImmutableList<string> eligibleRecipients
        )
    {
        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{organizerEmail}#HAT#{hatId}#PARTICIPANT" },
                ["SK"] = new() { S = $"PARTICIPANT#{participantEmail}" }
            },
            UpdateExpression = "SET EligibleParticipants = :eligibleRecipients",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":eligibleRecipients"] = new() { SS = eligibleRecipients.ToList() }
            }
        };

        await _dynamoDbClient
            .UpdateItemAsync(updateRequest)
            .ConfigureAwait(false);
    }

    public async Task DeleteParticipantAsync(string requestOrganizerEmail, Guid requestHatId, string requestEmail)
    {
        var deleteRequest = new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{requestOrganizerEmail}#HAT#{requestHatId}#PARTICIPANT" },
                ["SK"] = new() { S = $"PARTICIPANT#{requestEmail}" }
            }
        };

        await _dynamoDbClient
            .DeleteItemAsync(deleteRequest)
            .ConfigureAwait(false);
    }

    public async Task UpdateParticipantPickedRecipientAsync(
        string organizerEmail,
        Guid hatId,
        string participantEmail,
        string pickedRecipientName
        )
    {
        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{organizerEmail}#HAT#{hatId}#PARTICIPANT" },
                ["SK"] = new() { S = $"PARTICIPANT#{participantEmail}" }
            },
            UpdateExpression = "SET PickedRecipient = :pickedRecipient",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pickedRecipient"] = new() { S = pickedRecipientName }
            }
        };

        await _dynamoDbClient
            .UpdateItemAsync(updateRequest)
            .ConfigureAwait(false);
    }

    public async Task RemoveParticipantFromEligibleRecipientsAsync(
        string organizerEmail,
        Guid hatId,
        string participantNameToRemove
        )
    {
        var (hatExists, hat) = await GetHatAsync(organizerEmail, hatId)
            .ConfigureAwait(false);

        if (!hatExists)
            return;

        var participants = hat.Participants
            .Where(p => p.EligibleRecipients.Contains(participantNameToRemove, StringComparer.OrdinalIgnoreCase))
            .ToImmutableList();

        List<Task> updateTasks = [];

        // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
        foreach (var participant in participants)
        {
            var updatedEligibleRecipients = participant
                .EligibleRecipients
                .Where(r => !r.ContentEquals(participantNameToRemove))
                .ToImmutableList();

            updateTasks.Add(
                UpdateEligibleRecipientsAsync(
                    organizerEmail,
                    hatId,
                    participant.Person.Email,
                    updatedEligibleRecipients
                )
            );
        }

        await Task.WhenAll(updateTasks)
            .ConfigureAwait(false);
    }

    public async Task<DateTimeOffset> MarkInvitationsAsQueuedAsync(
        string organizerEmail,
        Guid hatId
        )
    {
        var invitationsQueuedDate = DateTimeOffset.UtcNow;

        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{organizerEmail}#HAT" },
                ["SK"] = new() { S = $"HAT#{hatId}" }
            },
            UpdateExpression = "SET HatStatus = :status, InvitationsQueuedDate = :invitationsQueuedDate",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = new() { S = HatStatus.InvitationsSent },
                [":invitationsQueuedDate"] = new() { S = invitationsQueuedDate.ToString("o") }
            }
        };

        await _dynamoDbClient
            .UpdateItemAsync(updateRequest)
            .ConfigureAwait(false);

        return invitationsQueuedDate;
    }

    public async Task TryTransitionHatToCooledOffAsync(
        string organizerEmail,
        Guid hatId
        )
    {
        var updateRequest = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"ORGANIZER#{organizerEmail}#HAT" },
                ["SK"] = new() { S = $"HAT#{hatId}" }
            },
            ConditionExpression = "HatStatus = :expectedStatus",
            UpdateExpression = "SET HatStatus = :newStatus",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":expectedStatus"] = new() { S = HatStatus.InvitationsSent },
                [":newStatus"] = new() { S = HatStatus.CooledOff }
            }
        };

        try
        {
            await _dynamoDbClient
                .UpdateItemAsync(updateRequest)
                .ConfigureAwait(false);
        }
        catch (ConditionalCheckFailedException)
        {
            _logger.LogError("Couldn't update HatStatus to to COOLED_OFF for hat {hatId}, since it does not have expected status. Will not retry.", hatId);
        }
    }
}
