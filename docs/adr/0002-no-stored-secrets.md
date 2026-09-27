# 0002 · Lapse stores no secrets

- Status: accepted
- Date: 2026-09-27

## Context

A tool that watches certificates and credentials could, for convenience, become yet another place where secrets pile up. That would make it a valuable target and nobody would want to install it.

## Decision

1. Lapse stores metadata only: names, dates, issuers and fingerprints.
2. Private keys are rejected at two points: when the configuration is validated (by extension) and when the file is read (by content). Every certificate read goes through `CertificateReader`, so the rule exists exactly once.
3. Lapse's own credentials (alert channels) are only accepted as `${env:…}` or `${file:…}` references, resolved in memory and wrapped in `Secret`, which prints as `***`.
4. The TLS probe always refuses the session during certificate validation: it reads the certificate and disconnects, without completing the handshake or sending data.

## Consequences

- The configuration can be committed to Git.
- A leaked database reveals the inventory but grants access to no system.
- Future sources that need read credentials (Entra ID, AWS) require their own encrypted storage design and a security review before being accepted.
