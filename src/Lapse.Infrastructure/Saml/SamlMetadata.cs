using System.Xml;
using System.Xml.Linq;
using Lapse.Core;

namespace Lapse.Infrastructure.Saml;

public sealed record SamlCertificate(string Use, byte[] Content);

internal static class SamlMetadata
{
    private static readonly XNamespace Metadata = "urn:oasis:names:tc:SAML:2.0:metadata";
    private static readonly XNamespace Signature = "http://www.w3.org/2000/09/xmldsig#";

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    public static Result<IReadOnlyList<SamlCertificate>> ParseCertificates(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Settings);
            var document = XDocument.Load(reader);

            var certificates = document.Descendants(Metadata + "KeyDescriptor")
                .SelectMany(descriptor => descriptor.Descendants(Signature + "X509Certificate")
                    .Select(element => new SamlCertificate(
                        (string?)descriptor.Attribute("use") ?? "signing",
                        Convert.FromBase64String(element.Value.Trim()))))
                .DistinctBy(certificate => Convert.ToBase64String(certificate.Content))
                .ToList();

            return certificates.Count > 0
                ? Result.Success<IReadOnlyList<SamlCertificate>>(certificates)
                : Result.Failure<IReadOnlyList<SamlCertificate>>("the metadata contains no X.509 certificates");
        }
        catch (Exception ex) when (ex is XmlException or FormatException)
        {
            return Result.Failure<IReadOnlyList<SamlCertificate>>("the response is not valid SAML metadata");
        }
    }
}
