# Security policy

## Supported versions

While Lapse is at `0.x`, only the latest release receives security fixes.

## Reporting a vulnerability

**Do not open a public issue.** Use GitHub private reporting:
the repository's **Security** tab → **Report a vulnerability**.

Include the version (`lapse --version`), the platform, steps to reproduce and the impact you see. Do not include passwords, tokens or real data from your infrastructure.

You will get an answer within 7 days. Once the fix is released, the advisory is made public and your contribution is credited if you wish.

## Scope

We are especially interested in:

- any way for a secret (SMTP password, webhook URL) to appear in the output, logs, database or an export;
- any way for Lapse to read or store a private key;
- outbound connections not documented in [docs/security.md](docs/security.md);
- code execution triggered by the configuration or by network responses.

The full security model is in [docs/security.md](docs/security.md).
