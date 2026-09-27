using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Lapse.Core;
using Lapse.Core.Items;

namespace Lapse.Infrastructure.Certificates;

public static class CertificateReader
{
    public const string PrivateKeyRejection =
        "The file contains a private key. Lapse only accepts the public part of a certificate (.cer, .crt, .pem).";

    private const long MaxFileBytes = 1024 * 1024;

    private static readonly HashSet<string> PrivateKeyExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pfx", ".p12", ".key" };

    public static bool HasPrivateKeyExtension(string path) => PrivateKeyExtensions.Contains(Path.GetExtension(path));

    public static async Task<Result<Observation>> ObserveFileAsync(ItemKey key, CancellationToken cancellationToken)
    {
        var path = key.Value;
        if (HasPrivateKeyExtension(path))
        {
            return Result.Failure<Observation>(PrivateKeyRejection);
        }

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return Result.Failure<Observation>("The file does not exist.");
        }

        if (file.Length > MaxFileBytes)
        {
            return Result.Failure<Observation>("The file is larger than 1 MB and does not look like a certificate.");
        }

        try
        {
            var content = await File.ReadAllBytesAsync(path, cancellationToken);
            return Observe(key, file.Name, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<Observation>("The file could not be read: access denied or in use.");
        }
    }

    public static Result<Observation> Observe(ItemKey key, string name, byte[] content)
    {
        try
        {
            if (ContainsPrivateKey(content))
            {
                return Result.Failure<Observation>(PrivateKeyRejection);
            }

            using var certificate = Load(content);
            if (certificate.HasPrivateKey)
            {
                return Result.Failure<Observation>(PrivateKeyRejection);
            }

            var expiresAt = new DateTimeOffset(certificate.NotAfter).ToUniversalTime();
            var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            return Result.Success(new Observation(key, name, expiresAt, fingerprint));
        }
        catch (CryptographicException)
        {
            return Result.Failure<Observation>("The content is not a valid X.509 certificate.");
        }
    }

    private static bool ContainsPrivateKey(byte[] content) =>
        content.AsSpan().IndexOf("PRIVATE KEY"u8) >= 0
        || X509Certificate2.GetCertContentType(content) == X509ContentType.Pkcs12;

    private static X509Certificate2 Load(byte[] content) =>
        content.AsSpan().IndexOf("-----BEGIN CERTIFICATE-----"u8) >= 0
            ? X509Certificate2.CreateFromPem(Encoding.ASCII.GetString(content))
            : X509CertificateLoader.LoadCertificate(content);
}
