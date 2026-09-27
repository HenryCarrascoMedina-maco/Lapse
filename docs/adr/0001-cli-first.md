# 0001 · A command-line tool before a web interface

- Status: accepted
- Date: 2026-09-27

## Context

Lapse watches sensitive information about an infrastructure. A web interface requires a server that accepts connections, authentication, sessions and protection against web attacks. It is the part with the largest attack surface and the most work, and it is not needed for a single person to get value.

## Decision

Version 0.1 is a CLI only. It opens no ports. Periodic execution is delegated to the system (cron, Task Scheduler, Docker) or to `lapse watch`.

Sources are defined by the `ISource` interface inside the codebase. Turning it into an external plugin protocol waits until the contract is proven by several real sources.

## Consequences

- Trivial installation and operation: one executable and one configuration file.
- Zero inbound network surface.
- `lapse check` and `--json` output allow integration with pipelines and other tools from day one.
- Anyone who needs a graphical view must wait for a later version or build it on top of `export` or the webhook.
