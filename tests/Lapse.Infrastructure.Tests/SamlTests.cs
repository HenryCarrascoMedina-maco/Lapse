using System.Net;
using Lapse.Core.Items;
using Lapse.Infrastructure.Saml;
using Lapse.Infrastructure.Sources;

namespace Lapse.Infrastructure.Tests;

public class SamlTests
{
    private const string MetadataUrl = "https://idp.example.com/metadata.xml";

    [Fact]
    public async Task Reads_every_certificate_published_in_the_metadata()
    {
        using var signing = TestCertificates.Create(Dates.Now.AddDays(40));
        using var encryption = TestCertificates.Create(Dates.Now.AddDays(400));
        var handler = StubHttpHandler.Responding(HttpStatusCode.OK, Metadata(
            ("signing", Base64(signing)), ("encryption", Base64(encryption)), ("signing", Base64(signing))));

        var result = await new SamlSource(new HttpClient(handler)).ObserveAsync(Target(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value.Count);
        Assert.StartsWith("idp.example.com · signing certificate ", result.Value[0].Name, StringComparison.Ordinal);
        Assert.StartsWith($"{MetadataUrl}#", result.Value[0].Key.Value, StringComparison.Ordinal);
        Dates.AssertSameSecond(Dates.Now.AddDays(40), result.Value[0].ExpiresAt);
        Dates.AssertSameSecond(Dates.Now.AddDays(400), result.Value[1].ExpiresAt);
    }

    [Fact]
    public void Document_type_definitions_are_refused()
    {
        var xml = """
            <?xml version="1.0"?>
            <!DOCTYPE md [ <!ENTITY secret SYSTEM "file:///etc/passwd"> ]>
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata">&secret;</md:EntityDescriptor>
            """;

        Assert.Equal("the response is not valid SAML metadata", SamlMetadata.ParseCertificates(xml).Error);
    }

    [Fact]
    public void Metadata_without_certificates_is_a_failure() =>
        Assert.Equal(
            "the metadata contains no X.509 certificates",
            SamlMetadata.ParseCertificates("""<md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" />""").Error);

    private static WatchTarget Target() => new(new ItemKey(ItemKind.Saml, MetadataUrl), "it");

    private static string Base64(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) =>
        Convert.ToBase64String(TestCertificates.Der(certificate));

    private static string Metadata(params (string Use, string Certificate)[] keys) => $"""
        <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" xmlns:ds="http://www.w3.org/2000/09/xmldsig#" entityID="https://idp.example.com">
          <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
            {string.Concat(keys.Select(key => $"""
              <md:KeyDescriptor use="{key.Use}"><ds:KeyInfo><ds:X509Data><ds:X509Certificate>{key.Certificate}</ds:X509Certificate></ds:X509Data></ds:KeyInfo></md:KeyDescriptor>
            """))}
          </md:IDPSSODescriptor>
        </md:EntityDescriptor>
        """;
}
