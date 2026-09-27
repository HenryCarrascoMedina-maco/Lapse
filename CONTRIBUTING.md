# Contributing

Thanks for helping improve Lapse. This guide covers the development setup, the code rules and how a change reaches `main`.

## Setup

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build
dotnet test
dotnet format --verify-no-changes
```

The tests need neither internet access nor external services.

## Workflow

1. Open an issue, or comment on an existing one, before starting a large change.
2. Branch from `main`: `feat/kubernetes-source`, `fix/rdap-timeout`, `docs/configuration`.
3. Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`.
4. Add a line under "Unreleased" in `CHANGELOG.md` if users will notice the change.
5. Open a pull request. CI builds, checks formatting and runs the tests on Windows, Linux and macOS. A pull request with failing CI is not reviewed.

## Code rules

- **The core depends on nothing.** `Lapse.Core` only uses the .NET base library. An architecture test fails if this breaks.
- **One path.** Every source implements `ISource` and returns an `Observation`; every alert goes through `AlertPlanner` and `INotifier`. No per-source shortcuts.
- **No duplication.** Logic needed by two classes lives in one place (for example, `CertificateReader` for any X.509 certificate). Build settings live in `Directory.Build.props` and package versions in `Directory.Packages.props`.
- **No unnecessary comments.** Code explains itself through names and small methods.
- **Time is injected.** Use `TimeProvider`, never `DateTime.Now`. Everything is stored in UTC.
- **Expected failures are not exceptions.** A host that does not answer returns `Result.Failure`. Exceptions are for programming errors.
- **Few dependencies.** Every new NuGet package needs an ADR in `docs/adr` explaining why the base library is not enough.
- **Tests without network.** Generate certificates and data inside the test.

Warnings are errors and the .NET analyzers are enabled. If a rule does not make sense in a specific place, suppress it there with a justification, not for the whole project.

## Adding a source

1. Open an issue with the "New source" template: what expires, how the date is read and which credentials it needs.
2. Add the value to `ItemKind` and a class implementing `ISource` in `src/Lapse.Infrastructure/Sources`.
3. Add its configuration section (`ConfigDocument` and `ConfigValidator`).
4. Register the source in `LapseRuntime`.
5. Write the tests and document any new outbound connection in `docs/security.md`.

Sources that need credentials (cloud APIs) also require a security review: least privilege, read only and no secret in the output.

## Security

Do not report vulnerabilities in public issues; follow [SECURITY.md](SECURITY.md).
