using System.Net;
using Lapse.Core.Items;
using Lapse.Infrastructure.Configuration;
using Lapse.Infrastructure.Entra;

namespace Lapse.Infrastructure.Tests;

public class EntraTests
{
    private const string Tenant = "contoso.onmicrosoft.com";
    private const string ClientSecret = "s3cret-value-that-must-not-leak";

    private const string FirstPage = """
        {
          "value": [
            {
              "appId": "11111111-aaaa",
              "displayName": "ERP Sync",
              "passwordCredentials": [
                { "keyId": "aaaa1111-2222", "displayName": "prod", "endDateTime": "2026-10-01T00:00:00Z", "hint": "abc" },
                { "keyId": "bbbb1111-2222", "displayName": null, "endDateTime": null, "hint": "xyz" }
              ],
              "keyCredentials": [
                { "keyId": "cccc1111-2222", "displayName": "CN=erp", "endDateTime": "2027-01-15T00:00:00Z" }
              ],
              "owners": [ { "mail": null, "userPrincipalName": "ana@contoso.com" } ]
            }
          ],
          "@odata.nextLink": "https://graph.microsoft.com/v1.0/applications?$skiptoken=next"
        }
        """;

    private const string SecondPage = """
        { "value": [ { "appId": "22222222-bbbb", "displayName": "Portal", "passwordCredentials": [
            { "keyId": "dddd1111-2222", "displayName": "", "endDateTime": "2026-12-01T00:00:00Z", "hint": "qwe" } ], "keyCredentials": [] } ] }
        """;

    [Fact]
    public void Graph_page_yields_secrets_and_certificates_with_their_owners()
    {
        var page = EntraGraph.ParseApplications(FirstPage, Tenant).Value;

        Assert.Equal(
            [$"{Tenant}/11111111-aaaa/secret/aaaa1111-2222", $"{Tenant}/11111111-aaaa/certificate/cccc1111-2222"],
            page.Items.Select(item => item.Key.Value));
        Assert.Equal(["ERP Sync · secret prod", "ERP Sync · certificate CN=erp"], page.Items.Select(item => item.Name));
        Assert.All(page.Items, item => Assert.Equal(["ana@contoso.com"], item.Contacts!));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), page.Items[0].ExpiresAt);
        Assert.Equal(new Uri("https://graph.microsoft.com/v1.0/applications?$skiptoken=next"), page.Next);
    }

    [Fact]
    public void Secret_hints_are_never_part_of_an_item() =>
        Assert.DoesNotContain(
            EntraGraph.ParseApplications(FirstPage, Tenant).Value.Items,
            item => item.Name.Contains("abc", StringComparison.Ordinal) || item.Key.Value.Contains("abc", StringComparison.Ordinal));

    [Fact]
    public async Task Reads_every_page_with_the_access_token()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host switch
        {
            "login.microsoftonline.com" => Json("""{ "access_token": "token-123", "token_type": "Bearer" }"""),
            _ when request.RequestUri.Query.Contains("skiptoken", StringComparison.Ordinal) => Json(SecondPage),
            _ => Json(FirstPage),
        });

        var result = await Source(handler).ObserveAsync(Target(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal("Portal · secret dddd1111", result.Value[2].Name);

        var token = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, token.Method);
        Assert.Contains($"client_secret={ClientSecret}", token.Body, StringComparison.Ordinal);
        Assert.All(handler.Requests.Skip(1), request =>
        {
            Assert.Equal("graph.microsoft.com", request.Uri.Host);
            Assert.Equal("Bearer token-123", request.Authorization);
            Assert.Null(request.Body);
        });
    }

    [Fact]
    public async Task Rejected_credentials_are_reported_without_the_secret()
    {
        var handler = StubHttpHandler.Responding(HttpStatusCode.Unauthorized, """{ "error": "invalid_client" }""");

        var result = await Source(handler).ObserveAsync(Target(), CancellationToken.None);

        Assert.Equal("Entra rejected the credentials of client client-id; check tenantId, clientId and the client secret", result.Error);
        Assert.DoesNotContain(ClientSecret, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_permission_is_explained()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "login.microsoftonline.com"
            ? Json("""{ "access_token": "token-123" }""")
            : new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await Source(handler).ObserveAsync(Target(), CancellationToken.None);

        Assert.Equal("the application needs the Application.Read.All application permission with admin consent", result.Error);
    }

    [Fact]
    public async Task Next_page_outside_graph_is_refused_so_the_token_never_leaves_microsoft()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "login.microsoftonline.com"
            ? Json("""{ "access_token": "token-123" }""")
            : Json("""{ "value": [], "@odata.nextLink": "https://attacker.example/steal" }"""));

        var result = await Source(handler).ObserveAsync(Target(), CancellationToken.None);

        Assert.Equal("Microsoft Graph pointed to a next page outside graph.microsoft.com", result.Error);
        Assert.DoesNotContain(handler.Requests, request => request.Uri.Host == "attacker.example");
    }

    private static EntraSource Source(StubHttpHandler handler) =>
        new(new HttpClient(handler), [new EntraTenant(Tenant, "client-id", new Secret(ClientSecret))]);

    private static WatchTarget Target() => new(new ItemKey(ItemKind.Entra, Tenant), "it");

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
}
