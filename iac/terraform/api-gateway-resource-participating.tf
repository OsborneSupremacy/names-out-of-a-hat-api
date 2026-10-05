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

# What the email links do, from the participant's own page. Static siblings of {email}, which API
# Gateway matches ahead of the variable. Like the organizer's POSTs, each takes the exchange in the
# body and the caller from the session authorizer.
resource "aws_api_gateway_resource" "participating-ideas-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_resource.participating-resource.id
  path_part   = "ideas"
}

resource "aws_api_gateway_resource" "participating-answer-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_resource.participating-resource.id
  path_part   = "answer"
}

resource "aws_api_gateway_resource" "participating-offer-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_resource.participating-resource.id
  path_part   = "offer"
}

resource "aws_api_gateway_resource" "participating-ask-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_resource.participating-resource.id
  path_part   = "ask"
}

resource "aws_api_gateway_resource" "participating-leave-resource" {
  rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  parent_id   = aws_api_gateway_resource.participating-resource.id
  path_part   = "leave"
}

module "gateway-options-response-participating-ideas" {
  source              = "./modules/gateway-options-response"
  gateway_rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  gateway_resource_id = aws_api_gateway_resource.participating-ideas-resource.id
}

module "gateway-options-response-participating-answer" {
  source              = "./modules/gateway-options-response"
  gateway_rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  gateway_resource_id = aws_api_gateway_resource.participating-answer-resource.id
}

module "gateway-options-response-participating-offer" {
  source              = "./modules/gateway-options-response"
  gateway_rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  gateway_resource_id = aws_api_gateway_resource.participating-offer-resource.id
}

module "gateway-options-response-participating-ask" {
  source              = "./modules/gateway-options-response"
  gateway_rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  gateway_resource_id = aws_api_gateway_resource.participating-ask-resource.id
}

module "gateway-options-response-participating-leave" {
  source              = "./modules/gateway-options-response"
  gateway_rest_api_id = aws_api_gateway_rest_api.giftexchange-gateway.id
  gateway_resource_id = aws_api_gateway_resource.participating-leave-resource.id
}
