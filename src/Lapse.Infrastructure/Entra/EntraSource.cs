using System.Net;
using System.Net.Http.Headers;
using Lapse.Core;
using Lapse.Core.Items;
using Lapse.Core.Scanning;
using Lapse.Infrastructure.Configuration;
using Lapse.Infrastructure.Http;

namespace Lapse.Infrastructure.Entra;

public sealed record EntraTenant(string TenantId, string ClientId, Secret ClientSecret);

public sealed class EntraSource(HttpClient http, IReadOnlyList<EntraTenant> tenants) : ISource
{
    private const string GraphHost = "graph.microsoft.com";

    private static readonly Uri FirstPage = new(
        "https://graph.microsoft.com/v1.0/applications?$select=appId,displayName,passwordCredentials,keyCredentials&$expand=owners($select=mail,userPrincipalName)&$top=100");

    private readonly Dictionary<string, EntraTenant> byTenant = tenants.ToDictionary(tenant => tenant.TenantId, StringComparer.OrdinalIgnoreCase);

    public ItemKind Kind => ItemKind.Entra;

    public async Task<Result<IReadOnlyList<Observation>>> ObserveAsync(WatchTarget target, CancellationToken cancellationToken)
    {
        if (!byTenant.TryGetValue(target.Key.Value, out var tenant))
        {
            return Failure("no credentials are configured for this tenant");
        }

        var token = await RequestTokenAsync(tenant, cancellationToken);
        if (!token.IsSuccess)
        {
            return Failure(token.Error);
        }

        var items = new List<Observation>();
        for (var page = FirstPage; page is not null;)
        {
            if (page.Scheme != Uri.UriSchemeHttps || !string.Equals(page.Host, GraphHost, StringComparison.OrdinalIgnoreCase))
            {
                return Failure("Microsoft Graph pointed to a next page outside graph.microsoft.com");
            }

            var current = page;
            var response = await HttpFetch.SendAsync(http, () => Authorized(current, token.Value), DescribeGraphFailure, cancellationToken);
            var parsed = response.Bind(json => EntraGraph.ParseApplications(json, tenant.TenantId));
            if (!parsed.IsSuccess)
            {
                return Failure(parsed.Error);
            }

            items.AddRange(parsed.Value.Items);
            page = parsed.Value.Next;
        }

        return Result.Success<IReadOnlyList<Observation>>(items);
    }

    private async Task<Result<string>> RequestTokenAsync(EntraTenant tenant, CancellationToken cancellationToken)
    {
        var endpoint = new Uri($"https://login.microsoftonline.com/{Uri.EscapeDataString(tenant.TenantId)}/oauth2/v2.0/token");
        var response = await HttpFetch.SendAsync(
            http,
            () => new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(
                [
                    new("client_id", tenant.ClientId),
                    new("client_secret", tenant.ClientSecret.Reveal()),
                    new("scope", "https://graph.microsoft.com/.default"),
                    new("grant_type", "client_credentials"),
                ]),
            },
            status => status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                ? $"Entra rejected the credentials of client {tenant.ClientId}; check tenantId, clientId and the client secret"
                : null,
            cancellationToken);

        return response.Bind(EntraGraph.ParseToken);
    }

    private static HttpRequestMessage Authorized(Uri uri, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string? DescribeGraphFailure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Microsoft Graph rejected the access token",
        HttpStatusCode.Forbidden => "the application needs the Application.Read.All application permission with admin consent",
        _ => null,
    };

    private static Result<IReadOnlyList<Observation>> Failure(string error) => Result.Failure<IReadOnlyList<Observation>>(error);
}
