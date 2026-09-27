using Lapse.Core;
using Lapse.Core.Items;
using Lapse.Infrastructure.Configuration;

namespace Lapse.Infrastructure.Tests;

public sealed class ConfigLoaderTests : IDisposable
{
    private const string SmtpPassword = "s3cret-that-must-not-leak";

    private readonly TempDirectory directory = new();
    private readonly Dictionary<string, string> environment = new() { ["LAPSE_SMTP_PASSWORD"] = SmtpPassword };

    [Fact]
    public async Task Loads_a_complete_configuration()
    {
        directory.Write("webhook.secret", "https://hooks.example.com/T0K3N\n");

        var result = await LoadAsync("""
            // Comments are allowed.
            {
              "owners": {
                "it": { "email": "it@example.com" },
                "accounting": { "email": "accounting@example.com", "backup": "it" }
              },
              "defaults": { "owner": "it", "warnAtDays": [30, 7] },
              "watch": {
                "hosts": [ "Example.com", { "target": "api.example.com:8443", "owner": "accounting" } ],
                "domains": [ "example.com." ],
                "files": [ { "path": "certs/signing.cer", "owner": "accounting" } ],
                "manual": [ { "name": "License", "expiresAt": "2027-01-31" } ],
              },
              "notify": {
                "email": { "host": "smtp.example.com", "user": "lapse@example.com", "password": "${env:LAPSE_SMTP_PASSWORD}" },
                "webhook": { "url": "${file:webhook.secret}" }
              }
            }
            """);

        Assert.True(result.IsSuccess, result.Error);
        var config = result.Value;
        Assert.Equal(
            ["tls:example.com:443", "tls:api.example.com:8443", "domain:example.com", $"file:{directory.File(Path.Combine("certs", "signing.cer"))}", "manual:License"],
            config.Plan.Targets.Select(target => target.Key.ToString()));
        Assert.Equal("accounting", config.Plan.Targets[1].Owner);
        Assert.Equal("signing.cer", config.Plan.Targets[3].Name);
        Assert.Equal(new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero), config.Plan.Targets[4].DeclaredExpiresAt);
        Assert.Equal([30, 7], config.Plan.Policy.WarnAtDays);
        Assert.Equal(587, config.Email!.Port);
        Assert.Equal(SmtpPassword, config.Email.Password!.Reveal());
        Assert.Equal("https://hooks.example.com/T0K3N", config.WebhookUrl!.Reveal());
    }

    [Fact]
    public async Task Rejects_a_password_written_in_plain_text_without_echoing_it()
    {
        var result = await LoadAsync(Minimal(notify: $$"""{ "email": { "host": "smtp.example.com", "user": "a@example.com", "password": "{{SmtpPassword}}" } }"""));

        Assert.Contains("notify.email.password: do not write secrets", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(SmtpPassword, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_a_webhook_url_written_in_plain_text()
    {
        var result = await LoadAsync(Minimal(notify: """{ "webhook": { "url": "https://hooks.example.com/T0K3N" } }"""));

        Assert.Contains("notify.webhook.url: do not write secrets", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("T0K3N", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_an_insecure_webhook()
    {
        environment["HOOK"] = "http://hooks.example.com/x";

        var result = await LoadAsync(Minimal(notify: """{ "webhook": { "url": "${env:HOOK}" } }"""));

        Assert.Contains("must use https", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_private_key_files_at_load_time()
    {
        var result = await LoadAsync(Minimal(watch: """{ "files": [ "signing.pfx" ] }"""));

        Assert.Contains("watch.files[0]: The file contains a private key", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_every_error_at_once()
    {
        var result = await LoadAsync(Minimal(watch: """{ "hosts": [ "bad host", "a.com", "a.com:443" ], "domains": [ "nodot" ], "manual": [ { "name": "x", "owner": "nobody", "expiresAt": "2027-01-01" } ] }"""));

        Assert.Contains("watch.hosts[0]", result.Error, StringComparison.Ordinal);
        Assert.Contains("watch.hosts[2]: 'a.com:443' is duplicated", result.Error, StringComparison.Ordinal);
        Assert.Contains("watch.domains[0]", result.Error, StringComparison.Ordinal);
        Assert.Contains("watch.manual[0]: owner 'nobody' is not defined", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_unknown_properties()
    {
        var result = await LoadAsync("""{ "owners": { "it": {} }, "wacth": {} }""");

        Assert.False(result.IsSuccess);
        Assert.Contains("wacth", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explains_how_to_create_a_missing_file()
    {
        var result = await ConfigLoader.LoadAsync(directory.File("missing.json"), Read, CancellationToken.None);

        Assert.Contains("lapse init", result.Error, StringComparison.Ordinal);
    }

    public void Dispose() => directory.Dispose();

    private static string Minimal(string watch = """{ "hosts": [ "example.com" ] }""", string notify = "{}") =>
        $$"""{ "owners": { "it": { "email": "it@example.com" } }, "defaults": { "owner": "it" }, "watch": {{watch}}, "notify": {{notify}} }""";

    private Task<Result<LapseConfiguration>> LoadAsync(string json) =>
        ConfigLoader.LoadAsync(directory.Write("lapse.json", json), Read, CancellationToken.None);

    private string? Read(string name) => environment.GetValueOrDefault(name);
}

public class ConfigValueTests
{
    [Theory]
    [InlineData("example.com", "example.com", 443)]
    [InlineData("API.Example.com:8443", "api.example.com", 8443)]
    [InlineData("10.0.0.5:636", "10.0.0.5", 636)]
    public void Host_endpoints_are_normalized(string text, string host, int port)
    {
        Assert.True(Sources.HostEndpoint.TryParse(text, out var endpoint));
        Assert.Equal(new Sources.HostEndpoint(host, port), endpoint);
    }

    [Theory]
    [InlineData("smtp://Mail.Example.com", "smtp://mail.example.com:587")]
    [InlineData("imap://mail.example.com", "imap://mail.example.com:143")]
    [InlineData("POP3://mail.example.com:1110", "pop3://mail.example.com:1110")]
    [InlineData("postgres://db.example.com", "postgres://db.example.com:5432")]
    public void Starttls_endpoints_use_the_protocol_default_port(string text, string expected)
    {
        Assert.True(Sources.HostEndpoint.TryParse(text, out var endpoint));
        Assert.Equal(expected, endpoint.ToString());
    }

    [Fact]
    public void Unknown_protocols_are_rejected() =>
        Assert.False(Sources.HostEndpoint.TryParse("ftp://files.example.com", out _));

    [Theory]
    [InlineData("")]
    [InlineData("host:0")]
    [InlineData("host:70000")]
    [InlineData("bad host:443")]
    public void Invalid_host_endpoints_are_rejected(string text) =>
        Assert.False(Sources.HostEndpoint.TryParse(text, out _));

    [Theory]
    [InlineData("Example.COM.", "example.com")]
    [InlineData("  acme.pe ", "acme.pe")]
    public void Domains_are_normalized(string text, string expected) =>
        Assert.Equal(expected, DomainName.Normalize(text).Value);

    [Fact]
    public void Secret_is_masked_when_printed()
    {
        var secret = new Secret("real-value");

        Assert.Equal("***", secret.ToString());
        Assert.Equal("***", $"{secret}");
        Assert.Equal("real-value", secret.Reveal());
    }

    [Fact]
    public void Unknown_secret_reference_is_rejected() =>
        Assert.False(SecretResolver.Resolve("${vault:x}", ".", _ => null).IsSuccess);

    [Fact]
    public void Missing_environment_variable_is_reported_by_name() =>
        Assert.Equal("environment variable MISSING_VARIABLE is not set", SecretResolver.Resolve("${env:MISSING_VARIABLE}", ".", _ => null).Error);

    [Fact]
    public void Item_keys_render_kind_and_value() =>
        Assert.Equal("domain:example.com", new ItemKey(ItemKind.Domain, "example.com").ToString());
}
