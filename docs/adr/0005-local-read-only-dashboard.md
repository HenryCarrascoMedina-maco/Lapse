# 0005 · A local, read-only web dashboard

- Status: proposed
- Date: 2026-11-01

## Context

ADR 0001 kept Lapse a CLI with no open ports, and pointed anyone who needs a graphical view to `export` or the webhook. Version 0.2 added `lapse report`, a static HTML file built from the local database. It answers "what expires soon", but it is a snapshot: it goes stale after the next scan, and it cannot show what the database already knows about each item, such as every observed expiration date and fingerprint (`observations`) or every alert sent (`alerts_sent`).

The dashboard is the most requested item in the roadmap. It is also the first feature that accepts connections, which is exactly the attack surface ADR 0001 avoided.

## Decision

Add `lapse serve`, a dashboard that is local and read-only by design:

- **Loopback only by default.** It listens on `127.0.0.1` (default port `7311`). Listening on any other address requires an explicit `--listen` and an access token, and prints a warning.
- **Read-only.** It only exposes `GET` routes over the existing store. It does not scan, edit items or send alerts. Those stay in the CLI, so the dashboard needs no CSRF protection, sessions or users.
- **No new dependencies.** A minimal HTTP/1.1 server on `TcpListener` from the base library, following ADR 0003. It serves a handful of fixed routes, so a general web framework is not needed, and it stays Native AOT compatible.
- **Same page, live data.** The dashboard reuses the `report.html` template and its Content-Security-Policy. The inventory is read from the database on every request, and the page adds an item detail view with the observation history (renewals) and the alerts sent.
- **Works with `watch`.** In Docker, `lapse watch --serve` scans periodically and serves the dashboard from the same process.

Routes:

| Route | Returns |
|---|---|
| `GET /` | The dashboard page |
| `GET /api/items` | The inventory, in the same shape as `lapse export` |
| `GET /api/items/{id}` | One item with its observations and alerts sent |
| `GET /health` | `200` if the database can be read |

## Consequences

- One more command, but the default installation still opens no ports: `serve` only listens when it is run.
- Requests carry no secrets, because Lapse does not store any (ADR 0002). An exposed dashboard leaks names, hosts and dates, so non-loopback access requires the token.
- The HTTP parsing is ours, and so are its bugs. It is covered by tests on malformed requests, oversized headers and unknown routes, and it rejects everything that is not one of the routes above.
- The README's "It opens no ports" becomes "It opens no ports unless you run `lapse serve`, and then only on 127.0.0.1".
- Write actions in the dashboard (scan now, acknowledge an alert) are left for a later ADR, because they need authentication and CSRF protection.
