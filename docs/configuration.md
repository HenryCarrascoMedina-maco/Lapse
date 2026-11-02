# Configuration

Lapse reads a JSON file, `lapse.json` in the current folder by default (change it with `-c`). Comments (`//`) and trailing commas are allowed. Unknown properties are an error, so typos such as `"pasword"` are caught.

`lapse init` creates an example file that runs as is. Its first line points to the [JSON schema](../lapse.schema.json), so editors such as VS Code complete and validate the file as you type:

```json
{ "$schema": "https://henrycarrascomedina-maco.github.io/Lapse/lapse.schema.json" }
```

## Structure

```jsonc
{
  "owners":   { ... },
  "defaults": { ... },
  "watch":    { ... },
  "notify":   { ... }
}
```

`notify` is optional.

## `owners`

Owners identified by a short name.

| Field | Required | Description |
|---|---|---|
| `email` | no | Address for email alerts. |
| `backup` | no | Another owner who also receives critical and expiration alerts. |

```json
"owners": {
  "it":         { "email": "it@acme.com" },
  "accounting": { "email": "accounting@acme.com", "backup": "it" }
}
```

## `defaults`

| Field | Default | Description |
|---|---|---|
| `owner` | — | Owner of the items that do not name one. |
| `warnAtDays` | `[30, 14, 7, 1]` | Days before expiry when an alert is sent. |
| `criticalDays` | `7` | From here on an item is critical and the alert also reaches the backup. |

Each alert is sent once per threshold. If a scan finds an item with 5 days left, it only sends the 7-day alert, not the 30- and 14-day ones. When the item is renewed, thresholds start over for the new date.

Besides the thresholds, Lapse sends these alerts:

| Alert | When | Recipients |
|---|---|---|
| Expired | The date has passed. | Owner and backup |
| Renewal verified | A scan sees a later date for an item someone was warned about. It is announced even if the first scan to see it was `check` or `scan --no-notify`. | Owner |
| Could not be verified | The item has failed every scan for about a day (host down, file missing, credentials rejected). Sent once until it recovers. | Owner |
| Certificate problem | The certificate is not valid for the host name, or a certificate of its chain expires before it. | Owner |

While an item cannot be read, its last observed date keeps counting down, so threshold alerts still arrive.

All the alerts of a scan are grouped: one email per group of recipients and one message per chat channel, most urgent first. Long digests are split so each message fits the channel: 8 alerts per Telegram message, 20 per Teams or webhook message.

## `watch`

Each section is a list. An entry can be a string or an object with its own `owner`.

### `hosts`

TLS certificates, as `host:port`. Without a port, 443 is used.

```json
"hosts": [ "acme.com", "api.acme.com:8443", { "target": "ldap.acme.local:636", "owner": "it" } ]
```

This works with any service that speaks TLS from the first byte: HTTPS, LDAPS (636), IMAPS (993), SMTPS (465). Lapse reads the certificate even if it is expired or untrusted.

Lapse also reports a certificate that is not valid for the host name (except for targets written as an IP address), and an intermediate certificate that expires before the server certificate. It does not report untrusted chains, because internal certificate authorities are common and the machine running Lapse may not trust them.

Services that upgrade a plain connection to TLS are written with their protocol:

| Target | Default port | Negotiation |
|---|---|---|
| `smtp://mail.acme.com` | 587 | `EHLO` and `STARTTLS` |
| `imap://mail.acme.com` | 143 | `STARTTLS` |
| `pop3://mail.acme.com` | 110 | `STLS` |
| `postgres://db.acme.com` | 5432 | `SSLRequest` |

Lapse stops right after the negotiation: it never authenticates or sends mail or queries. SQL Server and RDP are not supported yet.

### `domains`

Expiration date of the domain registration, looked up through RDAP.

```json
"domains": [ "acme.com", "acme.pe" ]
```

Some country-code extensions do not publish RDAP or do not include the date. In that case the scan says so: declare the domain as a manual item.

### `files`

Files holding the public part of a certificate: `.cer`, `.crt`, `.pem` or `.der`. Relative paths are resolved from the configuration folder.

```json
"files": [ { "path": "certificates/invoicing.cer", "owner": "accounting" } ]
```

This covers the digital signing certificate used for electronic invoicing (for example, the `.cer` of a Mexican SAT CSD). Only the `.cer` is needed: **never point to a `.pfx`, `.p12` or `.key`**. Lapse rejects them.

### `manual`

What cannot be discovered automatically: licenses, contracts, insurance policies.

| Field | Required | Description |
|---|---|---|
| `name` | yes | Item name. |
| `expiresAt` | yes | Date (`2026-12-24`) or ISO 8601 date and time. Without a time zone, UTC is assumed. |
| `owner` | no | Owner. |

### `saml`

Signing and encryption certificates published in the SAML metadata of an identity provider or service provider. When the identity provider's signing certificate expires, nobody can sign in.

```json
"saml": [ "https://idp.acme.com/metadata.xml", { "target": "https://login.partner.com/saml/metadata", "owner": "it" } ]
```

The URL must use HTTPS. Every certificate in the metadata becomes an item, such as `idp.acme.com · signing certificate 1A2B3C4D`. Providers usually publish the next certificate before rotating, so both appear until the old one is removed. The XML is read with document type definitions disabled.

### `entra`

Secrets and certificates of the application registrations in a Microsoft Entra ID tenant.

```json
"entra": [
  { "tenantId": "contoso.onmicrosoft.com", "clientId": "00000000-0000-0000-0000-000000000000",
    "clientSecret": "${env:LAPSE_ENTRA_SECRET}", "name": "Contoso", "owner": "it" }
]
```

| Field | Required | Description |
|---|---|---|
| `tenantId` | yes | Tenant ID or primary domain. |
| `clientId` | yes | Application (client) ID of the registration Lapse uses. |
| `clientSecret` | yes | **Reference** to that registration's client secret. |
| `name` | no | Name shown for the tenant when it cannot be read. |
| `owner` | no | Owner of the credentials whose application has no matching owner. |

To set it up, create an application registration for Lapse, grant it the **Application.Read.All** *application* permission, give admin consent and create a client secret. Lapse lists every application with its secrets and certificates; it never changes anything.

Each credential becomes an item such as `ERP Sync · secret prod`. If one of the application's owners in Entra has the same email as an owner in `owners`, that owner receives its alerts. If the tenant cannot be read, its items are kept and shown as `ERROR` instead of disappearing.

## `notify`

Without channels, `scan` works the same but sends no alerts.

### `email`

| Field | Default | Description |
|---|---|---|
| `host` | — | SMTP server. |
| `port` | `587` | Port. Lapse uses STARTTLS; implicit TLS on port 465 is not supported. |
| `user` | — | User name. If omitted, mail is sent without authentication (internal relay). |
| `password` | — | **Reference** to the password. Required when `user` is set. |
| `from` | `user` | Sender. |
| `useTls` | `true` | Only disable it for a local test relay. |

### `webhook`

| Field | Description |
|---|---|
| `url` | **Reference** to the URL. It must use HTTPS (`http://localhost` is allowed). |

Lapse sends one `POST` per scan with every pending alert:

```json
{
  "event": "alerts",
  "text": "[Lapse] api.acme.com:443 expires in 5 days, and 1 more",
  "content": "[Lapse] api.acme.com:443 expires in 5 days, and 1 more",
  "message": "2 alerts:

api.acme.com:443 expires in 5 days
  Item: ...",
  "alerts": [
    {
      "event": "threshold",
      "headline": "api.acme.com:443 expires in 5 days",
      "thresholdDays": 7,
      "item": {
        "kind": "tls", "key": "api.acme.com:443", "name": "api.acme.com:443",
        "owner": "it", "expiresAt": "2026-10-03T23:59:59+00:00", "daysRemaining": 5
      }
    }
  ]
}
```

The top-level `event` is `alerts`, or `test` for `lapse notify test`. Each alert's `event` is `threshold`, `expired`, `renewed`, `unverified` or `problem`. The item includes `error` when it could not be read and `problem` when its certificate has one. Slack and Mattermost display `text` and Discord displays `content`, so an incoming webhook from any of them works without changes.

### `teams`

| Field | Description |
|---|---|
| `url` | **Reference** to the URL of a Teams workflow "When a Teams webhook request is received". |

Lapse posts an Adaptive Card with the alert subject and details.

### `telegram`

| Field | Description |
|---|---|
| `botToken` | **Reference** to the token given by @BotFather. |
| `chatId` | Chat, group or channel that receives the alerts. |

## Testing the channels

`lapse notify test` sends a test message through every configured channel and says which ones failed and why. `check`, `list`, `export`, `report` and `discover` never send alerts, so they do not need the channel secrets. The test email goes to `defaults.owner`, or to the owner given with `--owner`. `--channel teams` tests a single channel.

## Secret references

The `notify.email.password`, `notify.webhook.url`, `notify.teams.url`, `notify.telegram.botToken` and `watch.entra[].clientSecret` fields only accept references:

| Reference | Reads |
|---|---|
| `${env:LAPSE_SMTP_PASSWORD}` | The `LAPSE_SMTP_PASSWORD` environment variable. |
| `${file:/run/secrets/lapse_smtp}` | The file content without the trailing newline. Relative to the configuration when not absolute. |

If you write the value directly, Lapse does not start and tells you how to fix it. This way the configuration can be committed to Git safely.
