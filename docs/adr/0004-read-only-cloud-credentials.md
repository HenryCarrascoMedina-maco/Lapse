# 0004 · Read-only cloud credentials, still never stored

- Status: accepted
- Date: 2026-09-27

## Context

Application secrets and certificates in Microsoft Entra ID are among the most common causes of outages in companies: an integration stops working the day its secret expires. Their expiry dates are not public; reading them requires a credential with access to Microsoft Graph. Decision 0002 says Lapse stores no secrets.

## Decision

1. Entra tenants are declared in `watch.entra` with a `clientSecret` that only accepts `${env:…}` or `${file:…}` references, exactly like the alert credentials. Lapse never writes it anywhere.
2. The application registration used by Lapse needs a single application permission: `Application.Read.All`. Lapse only reads; it never creates, rotates or deletes credentials.
3. Microsoft Graph returns the expiry date of each secret and certificate without its value. The `hint` field (the first characters of a secret) is ignored and never stored.
4. The client secret is only sent to `login.microsoftonline.com`. The access token is only sent to `graph.microsoft.com`; a pagination link pointing anywhere else stops the scan.
5. Graph is called over plain HTTP from the base library, without the Microsoft SDK, to keep dependencies minimal and Native AOT working.
6. When an application owner's email matches an owner in the configuration, that owner is responsible for the application's credentials; otherwise the target's owner is.

## Consequences

- Lapse can watch the credentials that break integrations most often, while the configuration can still be committed to Git.
- A leaked database reveals application names and dates, not credentials.
- Certificate-based authentication and managed identities, which avoid a client secret entirely, remain future work.
