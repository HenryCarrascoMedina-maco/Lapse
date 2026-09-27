using System.Globalization;
using System.Net.Mail;
using Lapse.Core;
using Lapse.Core.Alerts;
using Lapse.Core.Items;
using Lapse.Infrastructure.Certificates;
using Lapse.Infrastructure.Sources;

namespace Lapse.Infrastructure.Configuration;

internal sealed record TargetSpec(ItemKey Key, DateTimeOffset? ExpiresAt = null, string? DisplayName = null);

internal sealed class ConfigValidator(ConfigDocument document, string baseDirectory, Func<string, string?> readEnvironment)
{
    private const int DefaultSmtpPort = 587;

    private readonly List<string> errors = [];

    public Result<LapseConfiguration> Validate()
    {
        var owners = ReadOwners();
        var policy = ReadPolicy();
        var targets = ReadTargets(owners);
        var email = ReadEmail();
        var webhook = ReadWebhook();

        return errors.Count > 0
            ? Result.Failure<LapseConfiguration>("The configuration has errors:" + string.Concat(errors.Select(error => $"{Environment.NewLine}  - {error}")))
            : Result.Success(new LapseConfiguration(new WatchPlan(targets, owners, policy!), email, webhook, baseDirectory));
    }

    private Dictionary<string, Owner> ReadOwners()
    {
        var owners = new Dictionary<string, Owner>(StringComparer.Ordinal);
        foreach (var (name, owner) in document.Owners ?? new Dictionary<string, OwnerDocument>())
        {
            if (owner.Email is not null && !MailAddress.TryCreate(owner.Email, out _))
            {
                errors.Add($"owners.{name}.email: '{owner.Email}' is not a valid email address");
            }

            owners[name] = new Owner(name, owner.Email, owner.Backup);
        }

        foreach (var owner in owners.Values.Where(owner => owner.Backup is not null))
        {
            if (owner.Backup == owner.Name || !owners.ContainsKey(owner.Backup!))
            {
                errors.Add($"owners.{owner.Name}.backup: '{owner.Backup}' must be another owner defined in owners");
            }
        }

        return owners;
    }

    private AlertPolicy? ReadPolicy()
    {
        var warnAtDays = document.Defaults?.WarnAtDays ?? AlertPolicy.DefaultWarnAtDays;
        var criticalDays = document.Defaults?.CriticalDays ?? AlertPolicy.DefaultCriticalDays;

        if (warnAtDays.Count == 0 || warnAtDays.Any(days => days <= 0))
        {
            errors.Add("defaults.warnAtDays: must contain at least one positive number of days");
            return null;
        }

        if (criticalDays <= 0)
        {
            errors.Add("defaults.criticalDays: must be greater than zero");
            return null;
        }

        return new AlertPolicy(warnAtDays, criticalDays);
    }

    private List<WatchTarget> ReadTargets(Dictionary<string, Owner> owners)
    {
        var targets = new List<WatchTarget>();
        var seen = new HashSet<ItemKey>();
        var watch = document.Watch;

        AddTargets("hosts", watch?.Hosts, HostTarget);
        AddTargets("domains", watch?.Domains, DomainTarget);
        AddTargets("files", watch?.Files, FileTarget);
        AddTargets("manual", watch?.Manual, ManualTarget);

        if (targets.Count == 0 && errors.Count == 0)
        {
            errors.Add("watch: nothing to watch; add at least one host, domain, file or manual item");
        }

        return targets;

        void AddTargets(
            string section,
            IReadOnlyList<TargetDocument>? entries,
            Func<TargetDocument, Result<TargetSpec>> describe)
        {
            foreach (var (entry, index) in (entries ?? []).Select((entry, index) => (entry, index)))
            {
                var location = string.Create(CultureInfo.InvariantCulture, $"watch.{section}[{index}]");
                var description = describe(entry);
                var owner = entry.Owner ?? document.Defaults?.Owner;

                if (!description.IsSuccess)
                {
                    errors.Add($"{location}: {description.Error}");
                }
                else if (owner is null)
                {
                    errors.Add($"{location}: missing \"owner\" and there is no defaults.owner");
                }
                else if (!owners.ContainsKey(owner))
                {
                    errors.Add($"{location}: owner '{owner}' is not defined in owners");
                }
                else if (!seen.Add(description.Value.Key))
                {
                    errors.Add($"{location}: '{description.Value.Key.Value}' is duplicated");
                }
                else
                {
                    targets.Add(new WatchTarget(description.Value.Key, owner, description.Value.ExpiresAt, description.Value.DisplayName));
                }
            }
        }
    }

    private static Result<TargetSpec> HostTarget(TargetDocument entry) =>
        HostEndpoint.TryParse(entry.Target, out var endpoint)
            ? Result.Success<TargetSpec>(new TargetSpec(new ItemKey(ItemKind.Tls, endpoint.ToString())))
            : Result.Failure<TargetSpec>("expected host:port, for example \"acme.com:443\"");

    private static Result<TargetSpec> DomainTarget(TargetDocument entry) =>
        DomainName.Normalize(entry.Target).Map(domain => new TargetSpec(new ItemKey(ItemKind.Domain, domain)));

    private Result<TargetSpec> FileTarget(TargetDocument entry)
    {
        if ((entry.Path ?? entry.Target) is not { Length: > 0 } path)
        {
            return Result.Failure<TargetSpec>("missing \"path\"");
        }

        if (CertificateReader.HasPrivateKeyExtension(path))
        {
            return Result.Failure<TargetSpec>(CertificateReader.PrivateKeyRejection);
        }

        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, path));
        return Result.Success(new TargetSpec(new ItemKey(ItemKind.File, fullPath), DisplayName: Path.GetFileName(fullPath)));
    }

    private static Result<TargetSpec> ManualTarget(TargetDocument entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            return Result.Failure<TargetSpec>("missing \"name\"");
        }

        return DateTimeOffset.TryParse(entry.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiresAt)
            ? Result.Success<TargetSpec>(new TargetSpec(new ItemKey(ItemKind.Manual, entry.Name.Trim()), expiresAt.ToUniversalTime()))
            : Result.Failure<TargetSpec>("\"expiresAt\" must be a date, for example \"2026-12-24\"");
    }

    private EmailSettings? ReadEmail()
    {
        if (document.Notify?.Email is not { } email)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(email.Host))
        {
            errors.Add("notify.email.host: is required");
        }

        var from = email.From ?? email.User;
        if (from is null || !MailAddress.TryCreate(from, out _))
        {
            errors.Add("notify.email.from: must be a valid email address (or set \"user\" to one)");
        }

        Secret? password = null;
        if (email.User is not null)
        {
            password = ReadSecret("notify.email.password", email.Password, "LAPSE_SMTP_PASSWORD");
        }

        return new EmailSettings(email.Host ?? string.Empty, email.Port ?? DefaultSmtpPort, email.User, password, from ?? string.Empty, email.UseTls ?? true);
    }

    private Secret? ReadWebhook()
    {
        if (document.Notify?.Webhook is not { } webhook)
        {
            return null;
        }

        var url = ReadSecret("notify.webhook.url", webhook.Url, "LAPSE_WEBHOOK_URL");
        if (url is not null && !IsAllowedWebhook(url.Reveal()))
        {
            errors.Add("notify.webhook.url: the URL must use https (http is only allowed for localhost)");
        }

        return url;
    }

    private Secret? ReadSecret(string location, string? reference, string suggestedVariable)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            errors.Add($"{location}: is required; use ${{env:{suggestedVariable}}}");
            return null;
        }

        if (!SecretResolver.IsReference(reference))
        {
            errors.Add($"{location}: do not write secrets in the file; use a reference such as ${{env:{suggestedVariable}}} or ${{file:path}}");
            return null;
        }

        var secret = SecretResolver.Resolve(reference, baseDirectory, readEnvironment);
        if (!secret.IsSuccess)
        {
            errors.Add($"{location}: {secret.Error}");
            return null;
        }

        return secret.Value;
    }

    private static bool IsAllowedWebhook(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}
