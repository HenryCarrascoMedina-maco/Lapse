# Lapse

**Nothing in your organization should expire by surprise.**

Lapse watches TLS certificates, domains and certificate files, warns their owner before they expire and verifies that they were actually renewed. It runs on your own machine or server: it does not store passwords and does not send data to any Lapse service, because there is none.

```text
$ lapse scan
Scanning 8 items…

 STATUS    ITEM                      KIND    EXPIRES     DAYS  OWNER
 EXPIRED   intranet.acme.local:443   tls     2026-09-25    -2  it
 CRITICAL  api.acme.com:443          tls     2026-10-03     6  it
 WARNING   acme.pe                   domain  2026-10-24    27  finance
 RENEWED   acme.com:443              tls     2027-03-20   174  it       was: 2026-10-02
 ERROR     vpn.acme.com:443          tls     —              —  it       no response within 10 s

3 unchanged items are hidden (use --all)
Alerts sent: 3 · already sent before (skipped): 1
```

## Status

**Version 0.1.0**, the first public release. It works and is tested, but the configuration format may change before 1.0 (see [versioning](#versioning)).

| Works today | Coming next |
|---|---|
| TLS certificates of any `host:port` speaking TLS directly | Web interface |
| Domain expiration dates (RDAP) | Plugins in any language |
| `.cer`, `.crt`, `.pem` and `.der` files | STARTTLS (SMTP, IMAP), SQL Server, RDP |
| Manual items (licenses, contracts) | Entra ID, Azure Key Vault, AWS |
| Discovery through Certificate Transparency | PostgreSQL and multiple users |
| Owners, backups and alert thresholds | |
| Email and webhook alerts, never duplicated | |
| Renewal verification | |
| `lapse check` for CI pipelines | |

## Why it exists

Many outages are not caused by bugs but by something that expired: the API certificate, the company domain, the digital signing certificate used for invoicing. The date was written down from day one. What was missing was knowing the thing existed, someone being responsible for it, and someone checking the renewal.

Lapse closes that loop in four steps:

1. **Discover** what expires, including what nobody registered (`lapse discover`).
2. **Assign** each item to an owner with a backup.
3. **Alert** at 30, 14, 7 and 1 days. Critical alerts also reach the backup.
4. **Verify** the renewal: it only counts once a scan sees the new date.

## Security

- **Lapse stores no secrets.** It keeps metadata only: name, kind, expiry date, issuer and SHA-256 fingerprint.
- **It rejects private keys.** `.pfx`, `.p12` and `.key` files, and any PEM containing a private key, are rejected and never stored.
- **Alert credentials never go in the configuration file.** The SMTP password or webhook URL is written as a reference (`${env:NAME}` or `${file:path}`), exists only in memory and never appears in the output.
- **It only connects to what you declare:** your hosts, RDAP servers, crt.sh (only with `discover`) and your alert channel. It opens no ports.
- **The local database** (`lapse.db`) is created readable only by its owner on Linux and macOS.

The full model is in [docs/security.md](docs/security.md). To report a vulnerability, follow [SECURITY.md](SECURITY.md).

## Installation

**Executable.** Download the archive for your system from [Releases](https://github.com/HenryCarrascoMedina-maco/Lapse/releases), extract it and put `lapse` on your `PATH`. It is a single native executable: .NET does not need to be installed.

| System | Archive |
|---|---|
| Windows x64 | `lapse-v0.1.0-win-x64.zip` |
| Linux x64 | `lapse-v0.1.0-linux-x64.tar.gz` |
| macOS Apple Silicon | `lapse-v0.1.0-osx-arm64.tar.gz` |

Every release includes `SHA256SUMS` to verify the download and an SPDX SBOM.

**Docker.**

```sh
docker run --rm --read-only -v "$PWD:/data" ghcr.io/henrycarrascomedina-maco/lapse:0.1.0 scan
```

The image runs unprivileged and reads `lapse.json` from the `/data` volume, where it also keeps `lapse.db`.

**From source.** With the .NET 10 SDK:

```sh
dotnet run --project src/Lapse.Cli -- scan
```

## Five-minute tour

```sh
lapse init                      # creates an example lapse.json that runs as is
lapse scan                      # checks everything, stores the result and sends alerts
lapse discover your-domain.com  # certificates of your domain you are not watching yet
```

Then edit `lapse.json` with your owners, hosts, domains and files:

```jsonc
{
  "owners": {
    "it":         { "email": "it@acme.com" },
    "accounting": { "email": "accounting@acme.com", "backup": "it" }
  },
  "defaults": { "owner": "it", "warnAtDays": [30, 14, 7, 1] },
  "watch": {
    "hosts":   [ "acme.com", "api.acme.com:443", "ldap.acme.local:636" ],
    "domains": [ "acme.com", "acme.pe" ],
    "files":   [ { "path": "certificates/invoicing.cer", "owner": "accounting" } ],
    "manual":  [ { "name": "Antivirus license", "expiresAt": "2026-12-24" } ]
  },
  "notify": {
    "email":   { "host": "smtp.acme.com", "port": 587, "user": "lapse@acme.com",
                 "password": "${env:LAPSE_SMTP_PASSWORD}" },
    "webhook": { "url": "${env:LAPSE_WEBHOOK_URL}" }
  }
}
```

Full reference: [docs/configuration.md](docs/configuration.md).

## Commands

| Command | What it does |
|---|---|
| `lapse init` | Creates an example `lapse.json`. `--force` replaces it. |
| `lapse scan` | Scans, stores and sends pending alerts. `--all` shows everything, `--no-notify` sends nothing, `--json` prints JSON. |
| `lapse check` | Scans without alerting and fails if anything expires before `--min-days` (14 by default). Meant for CI. |
| `lapse list` | Shows the last stored inventory without connecting to anything. |
| `lapse export -o file.json` | Exports the inventory, for example as audit evidence. |
| `lapse discover domain` | Searches Certificate Transparency for certificates issued to the domain. |
| `lapse watch --every 6h` | Scans periodically and reloads the configuration on every cycle. |

Common options: `-c, --config` (defaults to `lapse.json`) and `--db` (defaults to `lapse.db` next to the configuration).

**Exit codes:** `0` success · `1` `check` found problems · `2` configuration error · `3` an alert could not be sent · `4` an external service did not answer.

### Scheduling

- **Windows:** `schtasks /Create /SC HOURLY /MO 6 /TN Lapse /TR "C:\lapse\lapse.exe scan -c C:\lapse\lapse.json"`
- **Linux or macOS (cron):** `0 */6 * * * /opt/lapse/lapse scan -c /etc/lapse/lapse.json`
- **Docker:** `docker run -d --read-only -v lapse:/data -e LAPSE_SMTP_PASSWORD ghcr.io/henrycarrascomedina-maco/lapse:0.1.0` (runs `watch --every 6h` by default).

### In a pipeline

```yaml
- name: Certificates and domains
  run: |
    curl -fsSL https://github.com/HenryCarrascoMedina-maco/Lapse/releases/download/v0.1.0/lapse-v0.1.0-linux-x64.tar.gz | tar -xz
    ./lapse check -c lapse.json --db "$RUNNER_TEMP/lapse.db" --min-days 14
```

## How it works

```text
lapse.json ──► ConfigLoader ──► WatchPlan
                                   │
             ┌─────────────────────┼─────────────────────┐
             ▼                     ▼                     ▼
        TlsSource             RdapSource        CertificateFileSource …   (ISource)
             └────────── Observation ───────────┘
                                   ▼
                              Reconciler  ──►  SQLite (lapse.db)
                                   ▼
                              AlertPlanner ──►  EmailNotifier · WebhookNotifier   (INotifier)
```

The code has three projects and one strict rule: **the core depends on nothing.**

| Project | Responsibility |
|---|---|
| `Lapse.Core` | Domain and rules: items, reconciliation, alert policy and planning. No external dependencies. |
| `Lapse.Infrastructure` | Implementations: TLS, RDAP, files, crt.sh, email, webhook, SQLite and configuration. |
| `Lapse.Cli` | Commands and wiring. |

Every source implements `ISource` and returns an `Observation`. All of them go through the same `Reconciler` and `AlertPlanner`, so adding a source means writing a single class. Design decisions are recorded in [docs/adr](docs/adr).

## Development

```sh
dotnet build
dotnet test
dotnet format --verify-no-changes
```

The tests need no internet access: they generate their own certificates and start a local TLS server.

## Versioning

Lapse follows [semantic versioning](https://semver.org/). While the version is `0.x`, configuration and commands may change; every change is recorded in [CHANGELOG.md](CHANGELOG.md). `1.0.0` will arrive once the configuration format and the source contract are stable.

## Contributing

Contributions are welcome, especially new sources. Read [CONTRIBUTING.md](CONTRIBUTING.md) and look for issues labeled `good first issue`.

## License

[Apache-2.0](LICENSE)
