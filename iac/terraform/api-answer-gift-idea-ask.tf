module "lambda-answer-gift-idea-ask" {
  source                                            = "./modules/api"
  gateway_rest_api_id                               = aws_api_gateway_rest_api.giftexchange-gateway.id
  gateway_resource_id                               = aws_api_gateway_resource.participating-answer-resource.id
  gateway_http_method                               = "PUT"
  gateway_http_operation_name                       = "AnswerGiftIdeaAsk"
  request_validator_id                              = aws_api_gateway_request_validator.body.id
  gateway_method_request_parameters                 = {}
  gateway_method_request_model_name                 = "AnswerGiftIdeaAskRequest"
  gateway_method_request_model_description          = "The caller's answer to an ask for gift ideas about somebody else."
  gateway_method_request_model_schema_file_location = "../../src/GiftExchange.Library/Schemas/AnswerGiftIdeaAskRequest.schema.json"
  include_404_response                              = true
  include_409_response                              = true
  good_response_model_name                          = ""
  good_response_model_description                   = ""
  good_response_model_schema_file_location          = ""
  lambda_invoke_arn                                 = aws_lambda_function.giftexchange_app.invoke_arn
  authorizer_id                                     = aws_api_gateway_authorizer.session.id
  authorizer_type                                   = "CUSTOM"
}
