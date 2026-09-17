using Amazon.Lambda.SQSEvents;
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
[JsonSerializable(typeof(CopyHatRequest))]
[JsonSerializable(typeof(CopyHatResponse))]
[JsonSerializable(typeof(CreateHatRequest))]
[JsonSerializable(typeof(CreateHatResponse))]
// The body of a message on the data deletion queue, read by DataDeletionQueueHandlerService.
[JsonSerializable(typeof(DataDeletionMessage))]
[JsonSerializable(typeof(DeleteMyDataRequest))]
[JsonSerializable(typeof(DeleteHatRequest))]
[JsonSerializable(typeof(EditHatRequest))]
[JsonSerializable(typeof(EditParticipantRequest))]
[JsonSerializable(typeof(EditParticipantAddressRequest))]
[JsonSerializable(typeof(EditParticipantEmojiRequest))]
[JsonSerializable(typeof(EditParticipantNameRequest))]
[JsonSerializable(typeof(EditParticipantAddressResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(ExportHatRequest))]
[JsonSerializable(typeof(ExportHatResponse))]
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
[JsonSerializable(typeof(APIGatewayCustomAuthorizerRequest))]
[JsonSerializable(typeof(APIGatewayCustomAuthorizerResponse))]
[JsonSerializable(typeof(RequestMagicLinkRequest))]
[JsonSerializable(typeof(RedeemMagicLinkRequest))]
[JsonSerializable(typeof(RedeemMagicLinkResponse))]
[JsonSerializable(typeof(RemoveParticipantRequest))]
[JsonSerializable(typeof(ResetHatRequest))]
[JsonSerializable(typeof(SendInvitationsRequest))]
// What SNS delivers to the delivery events queue. Deserialized by DeliveryEventsService rather
// than by the Lambda serializer -- the event this function is invoked with is an SQSEvent, and
// this is the shape of one record's body inside it.
[JsonSerializable(typeof(SesDeliveryEvent))]
// The assembly-level LambdaSerializer applies to every handler, so each entry point's event type
// has to be here. LambdaHandlerSerializationTests keeps that honest.
[JsonSerializable(typeof(SQSEvent))]
[JsonSerializable(typeof(StatusCodeOnlyResponse))]
[JsonSerializable(typeof(SubmitFeedbackRequest))]
[JsonSerializable(typeof(UndeliverableInvitationsScheduleRequest))]
[JsonSerializable(typeof(UpdateProfileRequest))]
[JsonSerializable(typeof(ValidateHatRequest))]
[JsonSerializable(typeof(ValidateHatResponse))]
internal partial class GiftExchangeJsonSerializerContext : JsonSerializerContext
{
}

public static class GiftExchangeJsonTypeInfoResolver
{
    public static IJsonTypeInfoResolver Default => GiftExchangeJsonSerializerContext.Default;
}
