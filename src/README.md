# Gift Exchange Application

- `GiftExchange.Library` — the API: handlers, services, providers, entities and schemas, deployed as Lambda functions.
- `GiftExchange.Library.Tests` — its unit tests, including the schema drift tests.
- `app` — the React frontend; see [app/README.md](app/README.md).

## Export the OpenAPI Description

This exports what is *deployed* to the `live` stage, so it is only ever true of that stage at the moment you run it. Read it and throw it away; do not commit it. A copy used to live at `docs/namesoutofahat_api.yaml` and was refreshed by hand every few months, which meant it spent almost all of its life describing an API that no longer existed — by the end it was missing the `/auth`, `/ask` and `/profile` endpoints entirely.

```bash
aws apigateway get-export \
  --region us-east-1 \
  --rest-api-id bz8vg16gqk \
  --stage-name live \
  --export-type oas30 \
  --accepts application/yaml \
  /tmp/namesoutofahat_api.yaml
```

The wire contract itself is not defined here. It lives in `GiftExchange.Library/Schemas/*.json`, which Terraform uploads as API Gateway models and which `SchemaDriftTests` holds to the records this application actually serializes. Those files are the ones to change, and the ones to trust: the export above is downstream of them and is reshaped by API Gateway on the way out, which flattens each schema's `definitions` into invented names like `HatParticipantsItem`.
