# Changelog

All notable changes to Lapse are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [semantic versioning](https://semver.org/).

## [Unreleased]

### Added

- STARTTLS targets: `smtp://`, `imap://`, `pop3://` and `postgres://`.
- `watch.saml`: signing and encryption certificates published in SAML metadata.
- `watch.entra`: secrets and certificates of Microsoft Entra ID application registrations, read with the `Application.Read.All` permission. Application owners whose email matches a configured owner receive their alerts.

### Changed

- A source can produce several items per target. When a target cannot be read, its items are kept and marked as not verified instead of being removed.

### Fixed

- Watching a subdomain in `domains` explains that only registered domains have an expiration date.

## [0.1.0] - 2026-09-27

First public release.

### Added

- Sources: TLS certificates of any `host:port`, domain expiration through RDAP, certificate files (`.cer`, `.crt`, `.pem`, `.der`) and manual items.
- `lapse discover`: certificate discovery through Certificate Transparency (crt.sh).
- Owners with backups, configurable alert thresholds and critical days.
- Email alerts (SMTP with STARTTLS) and a JSON webhook compatible with Slack, Mattermost and Discord. Each alert is sent once per channel and expiry date.
- Renewal verification: an alert is only resolved once a scan observes the new date.
- `init`, `scan`, `check`, `list`, `export`, `discover` and `watch` commands, with table or JSON output.
- Local SQLite database with embedded migrations.
- Rejection of private keys and of plain-text secrets in the configuration.
- Native executables (Native AOT) for Windows, Linux and macOS, and an unprivileged Docker image.

[Unreleased]: https://github.com/HenryCarrascoMedina-maco/Lapse/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/HenryCarrascoMedina-maco/Lapse/releases/tag/v0.1.0
