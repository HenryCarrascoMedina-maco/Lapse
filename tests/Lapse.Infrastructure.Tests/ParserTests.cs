using Lapse.Infrastructure.Discovery;
using Lapse.Infrastructure.Rdap;

namespace Lapse.Infrastructure.Tests;

public class RdapParserTests
{
    [Fact]
    public void Bootstrap_maps_each_tld_to_its_https_server()
    {
        var servers = RdapParser.ParseBootstrap("""
            { "services": [
                [["com", "net"], ["http://rdap.verisign.com/com/v1/", "https://rdap.verisign.com/com/v1/"]],
                [["pe"], ["https://rdap.nic.pe"]],
                [["old"], ["http://only-http.example/"]]
            ] }
            """).Value;

        Assert.Equal(new Uri("https://rdap.verisign.com/com/v1/"), servers["net"]);
        Assert.Equal(new Uri("https://rdap.nic.pe/"), servers["PE"]);
        Assert.False(servers.ContainsKey("old"));
    }

    [Fact]
    public void Expiration_event_is_read_as_utc()
    {
        var expiresAt = RdapParser.ParseExpiration("""
            { "events": [
                { "eventAction": "registration", "eventDate": "1995-08-14T04:00:00Z" },
                { "eventAction": "expiration", "eventDate": "2027-08-13T04:00:00Z" }
            ] }
            """).Value;

        Assert.Equal(new DateTimeOffset(2027, 8, 13, 4, 0, 0, TimeSpan.Zero), expiresAt);
    }

    [Theory]
    [InlineData("""{ "events": [ { "eventAction": "registration", "eventDate": "1995-08-14T04:00:00Z" } ] }""")]
    [InlineData("""{ "objectClassName": "domain" }""")]
    public void Missing_expiration_suggests_a_manual_item(string json) =>
        Assert.Contains("manual item", RdapParser.ParseExpiration(json).Error, StringComparison.Ordinal);

    [Fact]
    public void Malformed_response_is_a_failure() =>
        Assert.False(RdapParser.ParseExpiration("<html>").IsSuccess);
}

public class CertificateTransparencyTests
{
    [Fact]
    public void Keeps_the_latest_certificate_per_name_within_the_domain()
    {
        var names = CertificateTransparency.Parse("""
            [
              { "issuer_name": "C=US, O=Let's Encrypt, CN=R11", "name_value": "example.com\nwww.example.com", "not_after": "2026-10-01T00:00:00" },
              { "issuer_name": "C=US, O=Let's Encrypt, CN=R12", "name_value": "www.example.com", "not_after": "2026-12-01T00:00:00" },
              { "issuer_name": "CN=Other", "name_value": "*.example.com\nexample.org", "not_after": "2026-11-01T00:00:00" }
            ]
            """, "example.com").Value;

        Assert.Equal(["*.example.com", "example.com", "www.example.com"], names.Select(name => name.Name));
        var www = names.Single(name => name.Name == "www.example.com");
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), www.LatestExpiry);
        Assert.Equal("R12", www.Issuer);
        Assert.True(names[0].IsWildcard);
    }

    [Fact]
    public void Unexpected_format_is_a_failure() =>
        Assert.False(CertificateTransparency.Parse("""{ "error": true }""", "example.com").IsSuccess);
}
