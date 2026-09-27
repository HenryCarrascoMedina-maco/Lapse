using System.CommandLine;
using Lapse.Cli.Output;
using Lapse.Core;
using Lapse.Core.Alerts;
using Lapse.Core.Scanning;
using Lapse.Infrastructure.Configuration;
using Lapse.Infrastructure.Entra;
using Lapse.Infrastructure.Http;
using Lapse.Infrastructure.Notifications;
using Lapse.Infrastructure.Rdap;
using Lapse.Infrastructure.Sources;
using Lapse.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Lapse.Cli;

internal sealed class LapseRuntime : IAsyncDisposable
{
    private LapseRuntime(LapseConfiguration configuration, SqliteItemStore store, HttpClient http, ScanPipeline pipeline, TimeProvider time)
    {
        Time = time;
        Configuration = configuration;
        Store = store;
        Http = http;
        Pipeline = pipeline;
    }

    public LapseConfiguration Configuration { get; }

    public WatchPlan Plan => Configuration.Plan;

    public SqliteItemStore Store { get; }

    public HttpClient Http { get; }

    public ScanPipeline Pipeline { get; }

    public TimeProvider Time { get; }

    public static async Task<int> RunAsync(ParseResult parseResult, Func<LapseRuntime, Task<int>> action, CancellationToken cancellationToken)
    {
        var started = await StartAsync(parseResult, cancellationToken);
        if (!started.IsSuccess)
        {
            Terminal.Error(started.Error);
            return ExitCodes.ConfigurationError;
        }

        await using var runtime = started.Value;
        return await action(runtime);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await Store.DisposeAsync();
    }

    private static async Task<Result<LapseRuntime>> StartAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var loaded = await ConfigLoader.LoadAsync(
            parseResult.GetRequiredValue(GlobalOptions.Config),
            Environment.GetEnvironmentVariable,
            cancellationToken);

        if (!loaded.IsSuccess)
        {
            return Result.Failure<LapseRuntime>(loaded.Error);
        }

        var configuration = loaded.Value;
        var databasePath = Path.GetFullPath(parseResult.GetValue(GlobalOptions.Database) ?? Path.Combine(configuration.BaseDirectory, "lapse.db"));

        SqliteItemStore store;
        try
        {
            store = await SqliteItemStore.OpenAsync(databasePath, cancellationToken);
        }
        catch (SqliteException ex)
        {
            return Result.Failure<LapseRuntime>($"Could not open the database {databasePath}: {ex.Message}");
        }

        var time = TimeProvider.System;
        var http = LapseHttp.CreateClient();
        var bootstrapCache = Path.Combine(Path.GetDirectoryName(databasePath)!, "rdap-bootstrap.json");

        ISource[] sources =
        [
            new TlsSource(TlsSource.DefaultTimeout),
            new RdapSource(http, new RdapBootstrap(http, bootstrapCache, time)),
            new CertificateFileSource(),
            new ManualSource(),
            new EntraSource(http, configuration.EntraTenants),
        ];

        var pipeline = new ScanPipeline(sources, Notifiers(configuration, http, time), store, time);
        return Result.Success(new LapseRuntime(configuration, store, http, pipeline, time));
    }

    private static IEnumerable<INotifier> Notifiers(LapseConfiguration configuration, HttpClient http, TimeProvider time)
    {
        if (configuration.Email is { } email)
        {
            yield return new EmailNotifier(email);
        }

        if (configuration.WebhookUrl is { } url)
        {
            yield return new WebhookNotifier(http, url, time);
        }
    }
}
