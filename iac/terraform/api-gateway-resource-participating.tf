# The participant's side of /hats and /hat: the exchanges the caller has been invited to, and one
# of them read-only. Like those, the {email} segment is there so the route reads the same way; the
# caller is whoever the session authorizer says it is.
resource "aws_api_gateway_resource" "participating-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_rest_api.giftexchange-gateway.root_resource_id
  path_part   = "participating"
}

resource "aws_api_gateway_resource" "participating-email-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_resource.participating-resource.id
  path_part   = "{email}"
}

resource "aws_api_gateway_resource" "participating-email-id-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_resource.participating-email-resource.id
  path_part   = "{id}"
}

module "gateway-options-response-participating-email" {
  source              = "./modules/gateway-options-response"
  gateway_rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  gateway_resource_id = aws_api_gateway_resource.participating-email-resource.id
}

module "gateway-options-response-participating-email-id" {
  source              = "./modules/gateway-options-response"
  gateway_rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  gateway_resource_id = aws_api_gateway_resource.participating-email-id-resource.id
}
