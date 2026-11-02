using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Lapse.Core;
using Lapse.Infrastructure.Sources;

namespace Lapse.Infrastructure.Certificates;

internal sealed record ProbedCertificate(byte[] Content, string? Problem);

internal static class TlsProbe
{
    public static async Task<Result<ProbedCertificate>> FetchCertificateAsync(
        HostEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        byte[]? certificate = null;
        string? problem = null;

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = endpoint.Host,
            RemoteCertificateValidationCallback = (_, presented, chain, errors) =>
            {
                certificate = presented?.GetRawCertData();
                problem = DescribeProblem(endpoint.Host, chain, errors);
                return false;
            },
        };

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Host, endpoint.Port, timeoutSource.Token);
            var stream = client.GetStream();
            var negotiated = await StartTls.NegotiateAsync(stream, endpoint.Protocol, timeoutSource.Token);
            if (!negotiated.IsSuccess)
            {
                return Failure(negotiated.Error);
            }

            await using var tls = new SslStream(stream, leaveInnerStreamOpen: false);
            await tls.AuthenticateAsClientAsync(options, timeoutSource.Token);
        }
        catch (AuthenticationException) when (certificate is not null)
        {
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(string.Create(CultureInfo.InvariantCulture, $"no response within {timeout.TotalSeconds:0} s"));
        }
        catch (SocketException ex)
        {
            return Failure(DescribeSocketError(ex.SocketErrorCode));
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException)
        {
            return Failure("the service did not answer with TLS");
        }

        return certificate is null
            ? Failure("the server did not present a certificate")
            : Result.Success(new ProbedCertificate(certificate, problem));
    }

    // Trust errors are not reported: internal CAs are common and the machine running Lapse may not trust them.
    // A certificate for the wrong name, or a chain that expires first, breaks every client.
    private static string? DescribeProblem(string host, X509Chain? chain, SslPolicyErrors errors)
    {
        // Internal certificates rarely list the IP address a target may be written with.
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) && !IPAddress.TryParse(host, out _))
        {
            return $"the certificate is not valid for the name {host}";
        }

        if (chain is not { ChainElements.Count: > 1 })
        {
            return null;
        }

        // The root comes from this machine's store, which may differ from the clients' one, so only intermediates count.
        var certificates = chain.ChainElements.Select(element => element.Certificate).ToList();
        var leaf = certificates[0];
        var root = certificates[^1];
        var intermediates = root.SubjectName.RawData.AsSpan().SequenceEqual(root.IssuerName.RawData) ? certificates[1..^1] : certificates[1..];
        var issuer = intermediates
            .Where(certificate => certificate.NotAfter < leaf.NotAfter)
            .MinBy(certificate => certificate.NotAfter);

        return issuer is null
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"the chain certificate \"{issuer.GetNameInfo(X509NameType.SimpleName, forIssuer: false)}\" expires on {issuer.NotAfter.ToUniversalTime():yyyy-MM-dd}, before this certificate");
    }

    private static Result<ProbedCertificate> Failure(string error) => Result.Failure<ProbedCertificate>(error);

    private static string DescribeSocketError(SocketError error) => error switch
    {
        SocketError.HostNotFound or SocketError.NoData => "host not found",
        SocketError.ConnectionRefused => "connection refused",
        SocketError.TimedOut => "connection timed out",
        SocketError.NetworkUnreachable or SocketError.HostUnreachable => "network or host unreachable",
        _ => $"network error ({error})",
    };
}
