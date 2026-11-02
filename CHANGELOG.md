# Changelog

All notable changes to Lapse are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [semantic versioning](https://semver.org/).

## [Unreleased]

### Added

- `lapse notify test` sends a test message through every alert channel and explains why a channel fails.
- A JSON schema for `lapse.json`; `lapse init` references it so editors complete and validate the file.
- An alert when an item has failed every scan for a day, sent once until it recovers.
- TLS targets report a certificate that is not valid for the host name, or whose chain expires first. It raises an alert and makes `lapse check` fail.
- Alerts say what to do next, and name the source of items that come from a tenant or metadata URL.

### Changed

- **Breaking:** alerts are grouped: one email per group of recipients and one webhook, Teams or Telegram message per scan, split when it would be too long for the channel. The webhook payload now carries an `alerts` array instead of a single `item`.
- **Breaking:** in `scan --json`, each entry of `alerts.failures` lists its items in `keys` instead of a single `key`, and `alerts.unreachable` lists the items no channel can reach.
- A renewal is announced when the date observed before the new one had been warned about, so `check` and `scan --no-notify` no longer swallow it, and a channel that failed gets it on the next scan. Renewals nobody was warned about are not announced.
- `check`, `list`, `export`, `report` and `discover` no longer need the alert channel secrets, so `check` runs in CI with only the scanning secrets.
- `watch` keeps running when a cycle fails unexpectedly, and an unexpected error in one target no longer stops the whole scan.
- `scan` warns when an alert has no channel that reaches its owner, such as an owner without email when email is the only channel.
- The observation history only records new dates or certificates, instead of a row per scan.
- SMTP errors say whether the connection, TLS, the credentials or an address failed.

### Fixed

- An item that could not be read stopped receiving alerts, even when its last observed date was about to expire.

## [0.2.0] - 2026-09-27

### Added

- `lapse report`: a visual HTML report with status filters and search that works offline and loads nothing from the internet. `--from` builds it from an export file.
- A website with a live demo, published with GitHub Pages.
- STARTTLS targets: `smtp://`, `imap://`, `pop3://` and `postgres://`.
- Microsoft Teams (Adaptive Card through a workflow webhook) and Telegram alert channels.
- `watch.saml`: signing and encryption certificates published in SAML metadata.
- `watch.entra` (**beta**): secrets and certificates of Microsoft Entra ID application registrations, read with the `Application.Read.All` permission. Application owners whose email matches a configured owner receive their alerts.

### Changed

- A source can produce several items per target. When a target cannot be read, its items are kept and marked as not verified instead of being removed.

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

[Unreleased]: https://github.com/HenryCarrascoMedina-maco/Lapse/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/HenryCarrascoMedina-maco/Lapse/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/HenryCarrascoMedina-maco/Lapse/releases/tag/v0.1.0
