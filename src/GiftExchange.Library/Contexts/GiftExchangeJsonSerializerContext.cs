using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GiftExchange.Library.Contexts;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(APIGatewayProxyRequest))]
[JsonSerializable(typeof(APIGatewayProxyResponse))]
[JsonSerializable(typeof(AddParticipantRequest))]
[JsonSerializable(typeof(AssignRecipientsRequest))]
[JsonSerializable(typeof(CloseHatRequest))]
[JsonSerializable(typeof(CreateHatRequest))]
[JsonSerializable(typeof(CreateHatResponse))]
[JsonSerializable(typeof(DeleteHatRequest))]
[JsonSerializable(typeof(EditHatRequest))]
[JsonSerializable(typeof(EditParticipantRequest))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(GetHatRequest))]
[JsonSerializable(typeof(GetHatsRequest))]
[JsonSerializable(typeof(GetHatsResponse))]
[JsonSerializable(typeof(GetParticipantRequest))]
[JsonSerializable(typeof(GiftExchangeEmailRequest))]
[JsonSerializable(typeof(HatCooledOffScheduleRequest))]
[JsonSerializable(typeof(HatPreconditionRequest))]
[JsonSerializable(typeof(HatPreconditionResponse))]
[JsonSerializable(typeof(PreviewInvitationsRequest))]
[JsonSerializable(typeof(PreviewInvitationsResponse))]
[JsonSerializable(typeof(RemoveParticipantRequest))]
[JsonSerializable(typeof(SendInvitationsRequest))]
[JsonSerializable(typeof(StatusCodeOnlyResponse))]
[JsonSerializable(typeof(ValidateHatRequest))]
[JsonSerializable(typeof(ValidateHatResponse))]
internal partial class GiftExchangeJsonSerializerContext : JsonSerializerContext
{
}

public static class GiftExchangeJsonTypeInfoResolver
{
    public static IJsonTypeInfoResolver Default => GiftExchangeJsonSerializerContext.Default;
}
