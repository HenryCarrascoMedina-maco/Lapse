using System.Text;
using Lapse.Infrastructure.Certificates;

namespace Lapse.Infrastructure.Tests;

public sealed class CertificateReaderTests : IDisposable
{
    private readonly TempDirectory directory = new();
    private readonly DateTimeOffset notAfter = Dates.Now.AddDays(60);

    [Fact]
    public async Task Reads_expiry_and_fingerprint_from_a_der_file()
    {
        using var certificate = TestCertificates.Create(notAfter);
        var path = directory.Write("invoicing.cer", TestCertificates.Der(certificate));

        var result = await CertificateReader.ObserveFileAsync(path, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("invoicing.cer", result.Value.Name);
        Assert.Equal(64, result.Value.Fingerprint!.Length);
        Dates.AssertSameSecond(notAfter, result.Value.ExpiresAt);
    }

    [Fact]
    public async Task Reads_a_pem_file()
    {
        using var certificate = TestCertificates.Create(notAfter);
        var path = directory.Write("api.pem", TestCertificates.Pem(certificate));

        var result = await CertificateReader.ObserveFileAsync(path, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Dates.AssertSameSecond(notAfter, result.Value.ExpiresAt);
    }

    [Fact]
    public async Task Rejects_a_pem_that_contains_a_private_key()
    {
        using var certificate = TestCertificates.Create(notAfter);
        var path = directory.Write("bundle.pem", TestCertificates.PemWithPrivateKey(certificate));

        var result = await CertificateReader.ObserveFileAsync(path, CancellationToken.None);

        Assert.Equal(CertificateReader.PrivateKeyRejection, result.Error);
    }

    [Fact]
    public async Task Rejects_pkcs12_content_even_with_a_public_extension()
    {
        using var certificate = TestCertificates.Create(notAfter);
        var path = directory.Write("disguised.cer", TestCertificates.Pkcs12(certificate));

        var result = await CertificateReader.ObserveFileAsync(path, CancellationToken.None);

        Assert.Equal(CertificateReader.PrivateKeyRejection, result.Error);
    }

    [Theory]
    [InlineData("signing.pfx")]
    [InlineData("signing.P12")]
    [InlineData("server.key")]
    public async Task Rejects_private_key_extensions_without_reading_them(string name)
    {
        var path = directory.Write(name, "content that is never read");

        var result = await CertificateReader.ObserveFileAsync(path, CancellationToken.None);

        Assert.Equal(CertificateReader.PrivateKeyRejection, result.Error);
    }

    [Fact]
    public void Rejects_content_that_is_not_a_certificate() =>
        Assert.False(CertificateReader.Observe("x.cer", Encoding.ASCII.GetBytes("not a certificate")).IsSuccess);

    [Fact]
    public async Task Reports_a_missing_file()
    {
        var result = await CertificateReader.ObserveFileAsync(directory.File("missing.cer"), CancellationToken.None);

        Assert.Equal("The file does not exist.", result.Error);
    }

    public void Dispose() => directory.Dispose();
}
