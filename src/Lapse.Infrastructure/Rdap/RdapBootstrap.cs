using Lapse.Core;
using Lapse.Infrastructure.Http;

namespace Lapse.Infrastructure.Rdap;

public sealed class RdapBootstrap
{
    private static readonly Uri DirectoryUri = new("https://data.iana.org/rdap/dns.json");
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    private readonly HttpClient http;
    private readonly string cachePath;
    private readonly TimeProvider time;
    private readonly Lazy<Task<Result<IReadOnlyDictionary<string, Uri>>>> servers;

    public RdapBootstrap(HttpClient http, string cachePath, TimeProvider time)
    {
        this.http = http;
        this.cachePath = cachePath;
        this.time = time;
        servers = new(LoadAsync);
    }

    public async Task<Result<Uri>> FindServerAsync(string domain)
    {
        var directory = await servers.Value;
        if (!directory.IsSuccess)
        {
            return Result.Failure<Uri>(directory.Error);
        }

        var tld = domain[(domain.LastIndexOf('.') + 1)..];
        return directory.Value.TryGetValue(tld, out var server)
            ? Result.Success(server)
            : Result.Failure<Uri>($"the .{tld} extension has no RDAP service; declare it as a manual item");
    }

    private async Task<Result<IReadOnlyDictionary<string, Uri>>> LoadAsync()
    {
        var cache = new FileInfo(cachePath);
        var isFresh = cache.Exists && time.GetUtcNow() - cache.LastWriteTimeUtc < CacheLifetime;
        if (isFresh)
        {
            return RdapParser.ParseBootstrap(await File.ReadAllTextAsync(cachePath));
        }

        var downloaded = await HttpFetch.GetStringAsync(http, DirectoryUri, CancellationToken.None);
        if (downloaded.IsSuccess)
        {
            var parsed = RdapParser.ParseBootstrap(downloaded.Value);
            if (parsed.IsSuccess)
            {
                await WriteCacheAsync(downloaded.Value);
            }

            return parsed;
        }

        return cache.Exists
            ? RdapParser.ParseBootstrap(await File.ReadAllTextAsync(cachePath))
            : Result.Failure<IReadOnlyDictionary<string, Uri>>($"could not download the IANA RDAP directory: {downloaded.Error}");
    }

    private async Task WriteCacheAsync(string content)
    {
        var temporary = cachePath + ".tmp";
        await File.WriteAllTextAsync(temporary, content);
        File.Move(temporary, cachePath, overwrite: true);
    }
}
