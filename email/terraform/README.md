# Email infrastructure

SES identities and the DNS that supports them for `namesoutofahat.com`, moved here from the
standalone `names-out-of-a-hat-ses` repository.

Deliberately outside `iac/`, and deliberately a separate Terraform state (`giftexchange/email`)
with its own workflow. The two roots have different blast radii and different reasons to run.
`iac/` changes whenever a Lambda does, which is most deploys; this changes when email DNS does,
which is almost never — and when it goes wrong the failure is mail silently not arriving rather
than an API returning an error. Keeping them apart means a routine application deploy can never
plan a change to an MX record.

## State

The backend key is `giftexchange/email`, unchanged from the repository this came from. That is what
makes the move a move rather than a rebuild: Terraform reads the same state, finds every identity
and record already recorded in it, and plans nothing. A different key would have adopted nothing
and proposed creating a second copy of live DNS.

`iac/terraform/data.tf` reads this state through a `terraform_remote_state` data source, which is how `iac/` learns the configuration set and delivery events topic its functions use. The dependency runs one way: this root knows nothing about `iac/`. Removing an output is therefore the reverse of adding one: apply `iac/` first, so that nothing still reads it when it disappears.

## Deploying

Run the **Deploy Email Infrastructure** workflow by hand. It is `workflow_dispatch` only, for the
same reason `deploy-database.yml` is: these records are the difference between mail arriving and
mail vanishing, and a change to them deserves somebody deciding to make it.

## What is here

- `ses.tf` — the `mail.` identity, DKIM, custom MAIL FROM, and the receipt rule archiving everything
  arriving at `mail.namesoutofahat.com`.
- `route53.tf` — verification, DKIM, MX, SPF and DMARC records for `mail.`, plus the DMARC report
  authorization the apex record needs.
- `ses-delivery-events.tf` — the configuration set every outbound participant email names on the
  send, and the SNS topic SES publishes what became of it to. Without a configuration set on the
  request SES reports nothing at all, so this is what makes a bounced invitation distinguishable
  from a delivered one. No open or click tracking: both work by rewriting the message, and neither
  reports what it appears to.
- `s3.tf`, `sns.tf` — where received mail is archived and announced.

The receipt **rule set** these rules attach to, `ses-inbox-ruleset-main`, is not here. It is
provisioned in the `ahzborn-aws` repository, and a region permits exactly one active set, so
declaring one here would deactivate whatever is handling mail today.
