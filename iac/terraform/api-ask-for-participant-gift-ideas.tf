module "lambda-ask-for-participant-gift-ideas" {
  source                                            = "./modules/api"
  gateway_rest_api_id                               = aws_api_gateway_rest_api.giftexchange-gateway.id
  gateway_resource_id                               = aws_api_gateway_resource.participating-ask-resource.id
  gateway_http_method                               = "POST"
  gateway_http_operation_name                       = "AskForGiftIdeas"
  request_validator_id                              = aws_api_gateway_request_validator.body.id
  gateway_method_request_parameters                 = {}
  gateway_method_request_model_name                 = "AskForGiftIdeasRequest"
  gateway_method_request_model_description          = "A round of asking for gift ideas about the caller's pick."
  gateway_method_request_model_schema_file_location = "../../src/GiftExchange.Library/Schemas/AskForGiftIdeasRequest.schema.json"
  include_404_response                              = true
  include_409_response                              = true
  good_response_model_name                          = "AskForGiftIdeasResponse"
  good_response_model_description                   = "What a round of asking did."
  good_response_model_schema_file_location          = "../../src/GiftExchange.Library/Schemas/AskForGiftIdeasResponse.schema.json"
  lambda_invoke_arn                                 = aws_lambda_function.giftexchange_app.invoke_arn
  authorizer_id                                     = aws_api_gateway_authorizer.session.id
  authorizer_type                                   = "CUSTOM"
}
