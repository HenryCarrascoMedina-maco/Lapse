using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Lapse.Core.Items;
using Lapse.Infrastructure.Sources;

namespace Lapse.Infrastructure.Tests;

public sealed class TlsSourceTests : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly TlsSource source = new(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Reads_the_certificate_presented_by_a_tls_server()
    {
        var notAfter = Dates.Now.AddDays(45);
        using var certificate = TestCertificates.Create(notAfter);
        var server = ServeAsync(certificate);

        var result = await source.ObserveAsync(Target(Port), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Dates.AssertSameSecond(notAfter, Assert.Single(result.Value).ExpiresAt);
        await server;
    }

    [Fact]
    public async Task Reads_expired_certificates_too()
    {
        var notAfter = Dates.Now.AddYears(-1);
        using var certificate = TestCertificates.Create(notAfter);
        var server = ServeAsync(certificate);

        var result = await source.ObserveAsync(Target(Port), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Dates.AssertSameSecond(notAfter, Assert.Single(result.Value).ExpiresAt);
        await server;
    }

    [Fact]
    public async Task Reports_a_refused_connection()
    {
        listener.Start();
        var closedPort = Port;
        listener.Stop();

        var result = await source.ObserveAsync(Target(closedPort), CancellationToken.None);

        Assert.Equal("connection refused", result.Error);
    }

    public ValueTask DisposeAsync()
    {
        listener.Dispose();
        return ValueTask.CompletedTask;
    }

    private int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    private static WatchTarget Target(int port) => new(new ItemKey(ItemKind.Tls, $"127.0.0.1:{port}"), "it");

    private Task ServeAsync(X509Certificate2 certificate)
    {
        listener.Start();
        return Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var tls = new SslStream(client.GetStream());
            try
            {
                await tls.AuthenticateAsServerAsync(certificate);
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException)
            {
            }
        });
    }
}
