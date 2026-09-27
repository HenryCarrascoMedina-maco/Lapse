using System.Net;
using Lapse.Core.Items;
using Lapse.Infrastructure.Rdap;
using Lapse.Infrastructure.Sources;

namespace Lapse.Infrastructure.Tests;

public sealed class RdapSourceTests : IDisposable
{
    private const string Bootstrap = """{ "services": [ [["app", "com"], ["https://rdap.example/"]] ] }""";

    private readonly TempDirectory directory = new();

    [Fact]
    public async Task Reads_the_expiration_of_a_registered_domain()
    {
        var source = Source(HttpStatusCode.OK, """{ "events": [ { "eventAction": "expiration", "eventDate": "2031-05-08T00:00:00Z" } ] }""");

        var result = await source.ObserveAsync(Target("netlify.app"), CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2031, 5, 8, 0, 0, 0, TimeSpan.Zero), result.Value.ExpiresAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Explains_that_a_subdomain_is_not_a_registered_domain(HttpStatusCode status)
    {
        var result = await Source(status).ObserveAsync(Target("henry.netlify.app"), CancellationToken.None);

        Assert.Equal(
            "the registry has no domain named henry.netlify.app; if it is a subdomain, watch its registered domain instead, such as netlify.app",
            result.Error);
    }

    [Fact]
    public async Task Reports_an_unknown_registered_domain_without_a_suggestion()
    {
        var result = await Source(HttpStatusCode.NotFound).ObserveAsync(Target("nobody.com"), CancellationToken.None);

        Assert.Equal("the registry has no domain named nobody.com", result.Error);
    }

    public void Dispose() => directory.Dispose();

    private RdapSource Source(HttpStatusCode status, string body = "")
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host == "data.iana.org"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Bootstrap) }
            : new HttpResponseMessage(status) { Content = new StringContent(body) });
        var http = new HttpClient(handler);
        return new RdapSource(http, new RdapBootstrap(http, directory.File("rdap-bootstrap.json"), new FixedTime(Dates.Now)));
    }

    private static WatchTarget Target(string domain) => new(new ItemKey(ItemKind.Domain, domain), "it");
}
