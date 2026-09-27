using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Lapse.Infrastructure.Tests;

internal static class TestCertificates
{
    public static X509Certificate2 Create(DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=lapse.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(notAfter.AddDays(-30), notAfter);

        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), password: null, X509KeyStorageFlags.Exportable);
    }

    public static byte[] Der(X509Certificate2 certificate) => certificate.Export(X509ContentType.Cert);

    public static string Pem(X509Certificate2 certificate) => certificate.ExportCertificatePem();

    public static string PemWithPrivateKey(X509Certificate2 certificate) =>
        certificate.ExportCertificatePem() + "\n" + certificate.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem();

    public static byte[] Pkcs12(X509Certificate2 certificate) => certificate.Export(X509ContentType.Pkcs12);
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lapse-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string Write(string name, string content)
    {
        System.IO.File.WriteAllText(File(name), content);
        return File(name);
    }

    public string Write(string name, byte[] content)
    {
        System.IO.File.WriteAllBytes(File(name), content);
        return File(name);
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(Uri Uri, string? Body)> Requests { get; } = [];

    public static StubHttpHandler Responding(HttpStatusCode status, string body = "") =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!, body));
        return respond(request);
    }
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class Dates
{
    public static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    public static void AssertSameSecond(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.True(Math.Abs((expected - actual).TotalSeconds) < 1, $"Expected {expected:O} but got {actual:O}.");
}
