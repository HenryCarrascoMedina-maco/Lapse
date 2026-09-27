# Security model

This document describes what Lapse stores, where it connects and how it handles the only secret it deals with. If something does not behave as described here, report it according to [SECURITY.md](../SECURITY.md).

## Principles

1. **Lapse watches dates, not secrets.** Almost everything that expires publishes its date openly or read-only: a server hands its certificate to anyone who connects, and a domain's expiration is public in RDAP.
2. **Everything is local.** Lapse has no server, no accounts and no telemetry. Each installation is independent and its data lives on its own machine.
3. **Minimal surface.** Version 0.1 is a command-line tool: it opens no ports and accepts no inbound connections.

## What is stored

The `lapse.db` database contains metadata only:

| Table | Content |
|---|---|
| `items` | Kind, key (host:port, domain, path or name), name, owner, expiry date, status and last error. |
| `observations` | Every observed date, with the certificate's SHA-256 fingerprint. |
| `alerts_sent` | Which alert was sent, through which channel and when. |

Even without secrets, an inventory of certificates and domains says a lot about an infrastructure. Therefore:

- on Linux and macOS, `lapse.db` is created with `0600` permissions (owner only);
- on Windows it inherits the folder permissions, so place it in a restricted folder;
- it is excluded from Git in `.gitignore`.

## What is never stored

- Private keys. `.pfx`, `.p12` and `.key` files are rejected when the configuration is loaded and again when read. Any file whose content holds a private key (PEM with `PRIVATE KEY` or a PKCS#12 container) is rejected without being stored.
- Passwords, tokens or API keys of the watched systems.
- Lapse's own alert credentials (see below).

## Outbound connections

Lapse only connects to these destinations:

| Destination | When | What is sent |
|---|---|---|
| The hosts in `watch.hosts` | `scan`, `check`, `watch` | A TLS hello. Lapse reads the presented certificate and drops the connection without completing the session or sending data. |
| `data.iana.org` | When domains are configured, at most once every 7 days | Nothing; it downloads the RDAP server directory and caches it in `rdap-bootstrap.json`. |
| The RDAP server of each extension | When domains are configured | The domain name. |
| `crt.sh` | Only with `lapse discover` | The queried domain. |
| The URLs in `watch.saml` | `scan`, `check`, `watch` | Nothing; Lapse downloads the public metadata. |
| `login.microsoftonline.com` | When Entra tenants are configured | The client ID and client secret, to obtain an access token. |
| `graph.microsoft.com` | When Entra tenants are configured | The access token. Lapse refuses pagination links to any other host. |
| Your SMTP server, webhook, Teams workflow or `api.telegram.org` | When sending alerts | The alert content. |

Querying RDAP or crt.sh tells those services which domains you care about. The data is public, but if that is a concern, do not declare domains and do not use `discover`.

Lapse does not sweep networks or scan ports: it only contacts the targets you declare, with a 10-second timeout and at most 8 concurrent connections.

## Credentials

Lapse handles four kinds of credentials: the SMTP password, webhook and Teams URLs carrying a token, the Telegram bot token, and the client secret of an Entra application registration. The Entra credential only needs the read-only `Application.Read.All` permission; Microsoft Graph returns credential expiry dates without their values, and Lapse ignores the `hint` field that holds the first characters of a secret. All of them are treated the same way:

- **Never in the configuration file.** Those fields only accept a reference:
  - `${env:NAME}` reads an environment variable;
  - `${file:path}` reads a file, relative to the configuration (useful with Docker secrets in `/run/secrets`).
  If the value is not a reference, Lapse refuses to start and explains how to fix it, without repeating the value.
- **Never in the database or in exports.** They are resolved when the configuration loads and exist only in memory.
- **Never in the output.** They are held in a `Secret` type that prints as `***`. Webhook errors never include its URL.
- **Webhooks over HTTPS only**, except `http://localhost` for local testing.

## Reports

`lapse report` writes a single HTML file. It declares a Content Security Policy that blocks every network request, uses no external fonts, scripts or images, and inserts item names as text so that a crafted certificate or application name cannot inject code. Treat the file like the database: it lists your inventory.

## Supply chain

- Minimal dependencies: `System.CommandLine` and `Microsoft.Data.Sqlite`. Everything else comes with .NET.
- Versions pinned centrally in `Directory.Packages.props` and kept up to date by Dependabot.
- CodeQL analyzes every change.
- Every release publishes SHA-256 checksums and an SPDX SBOM.

## Container

The image is based on Microsoft's *chiseled* `runtime-deps`: no shell, no package manager, and it runs as the unprivileged `app` user. Run it with `--read-only` and a volume for `/data` only.
