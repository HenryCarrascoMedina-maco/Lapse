# 0003 · Minimal, Native AOT compatible dependencies

- Status: accepted
- Date: 2026-09-27

## Context

Every dependency is third-party code running with network and disk access on the user's machine. Lapse also ships as a native executable (Native AOT), which rules out reflection-based libraries.

## Decision

Only two runtime packages:

- `System.CommandLine`, the official .NET argument parser.
- `Microsoft.Data.Sqlite`, the official SQLite provider.

Everything else uses the base library: `SslStream` for TLS, `X509Certificate2` for certificates, `HttpClient`, `System.Text.Json` with source generation and `SmtpClient` for email.

Rejected:

- **Entity Framework Core**: three tables do not justify an ORM, and its AOT support is still limited. Lapse uses explicit parameterized SQL and embedded `.sql` migrations.
- **MailKit**: more complete than `SmtpClient`, but the base library is enough to send a message over STARTTLS. To be reconsidered if implicit TLS (port 465) or OAuth is needed.
- **Dependency injection containers**: manual wiring in `LapseRuntime` takes a few lines and is easier to follow.

## Consequences

- A single executable of about 9 MB, with no runtime, that starts instantly.
- A smaller supply-chain surface.
- Any new package requires an ADR explaining why the base library is not enough.
