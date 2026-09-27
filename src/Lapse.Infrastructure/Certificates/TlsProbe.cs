using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Lapse.Core;
using Lapse.Infrastructure.Sources;

namespace Lapse.Infrastructure.Certificates;

internal static class TlsProbe
{
    public static async Task<Result<byte[]>> FetchCertificateAsync(
        HostEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        byte[]? certificate = null;

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = endpoint.Host,
            RemoteCertificateValidationCallback = (_, presented, _, _) =>
            {
                certificate = presented?.GetRawCertData();
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

        return certificate is null ? Failure("the server did not present a certificate") : Result.Success(certificate);
    }

    private static Result<byte[]> Failure(string error) => Result.Failure<byte[]>(error);

    private static string DescribeSocketError(SocketError error) => error switch
    {
        SocketError.HostNotFound or SocketError.NoData => "host not found",
        SocketError.ConnectionRefused => "connection refused",
        SocketError.TimedOut => "connection timed out",
        SocketError.NetworkUnreachable or SocketError.HostUnreachable => "network or host unreachable",
        _ => $"network error ({error})",
    };
}
