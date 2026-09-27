# Changelog

All notable changes to Lapse are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [semantic versioning](https://semver.org/).

## [Unreleased]

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
