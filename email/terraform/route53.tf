# Domain verification record
resource "aws_route53_record" "ses_verification" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "_amazonses.mail.namesoutofahat.com"
  type    = "TXT"
  ttl     = 600
  records = [aws_ses_domain_identity.mail.verification_token]
}

# DKIM records
resource "aws_route53_record" "dkim" {
  count   = 3
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "${element(aws_ses_domain_dkim.mail.dkim_tokens, count.index)}._domainkey.mail.namesoutofahat.com"
  type    = "CNAME"
  ttl     = 600
  records = ["${element(aws_ses_domain_dkim.mail.dkim_tokens, count.index)}.dkim.amazonses.com"]
}

# MX record for receiving emails
resource "aws_route53_record" "mx" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "mail.namesoutofahat.com"
  type    = "MX"
  ttl     = 600
  records = ["10 inbound-smtp.us-east-1.amazonaws.com"]
}

# SPF record
resource "aws_route53_record" "spf" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "mail.namesoutofahat.com"
  type    = "TXT"
  ttl     = 600
  records = ["v=spf1 include:amazonses.com ~all"]
}

# Custom MAIL FROM MX record for SES bounce handling
resource "aws_route53_record" "mail_from_mx" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = aws_ses_domain_mail_from.mail.mail_from_domain
  type    = "MX"
  ttl     = 600
  records = ["10 feedback-smtp.us-east-1.amazonses.com"]
}

# Custom MAIL FROM SPF record for SES
resource "aws_route53_record" "mail_from_spf" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = aws_ses_domain_mail_from.mail.mail_from_domain
  type    = "TXT"
  ttl     = 600
  records = ["v=spf1 include:amazonses.com -all"]
}

# DMARC record
resource "aws_route53_record" "dmarc" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "_dmarc.mail.namesoutofahat.com"
  type    = "TXT"
  ttl     = 600
  records = ["v=DMARC1; p=quarantine; rua=mailto:dmarc@mail.namesoutofahat.com"]
}

# Authorizes mail.namesoutofahat.com to receive DMARC aggregate reports about the apex domain.
#
# The organizational record below sends its rua to an address on mail.namesoutofahat.com, which is
# a different domain from the one being reported on, and RFC 7489 section 7.1 requires the
# receiving domain to say it accepts that. Without this record a strict reporting receiver just
# does not send the report, silently — so the apex has been asking for aggregate reports that the
# larger mailbox providers were entitled to withhold.
resource "aws_route53_record" "dmarc_root_report_authorization" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "namesoutofahat.com._report._dmarc.mail.namesoutofahat.com"
  type    = "TXT"
  ttl     = 600
  records = ["v=DMARC1"]
}

# Organizational DMARC record (applies to root-domain From addresses)
resource "aws_route53_record" "dmarc_root" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "_dmarc.namesoutofahat.com"
  type    = "TXT"
  ttl     = 600
  records = ["v=DMARC1; p=quarantine; rua=mailto:dmarc@mail.namesoutofahat.com; adkim=r; aspf=r"]
}
