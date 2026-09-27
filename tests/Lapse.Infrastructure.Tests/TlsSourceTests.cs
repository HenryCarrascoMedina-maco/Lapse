using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

        var result = await source.ObserveAsync(Target(), CancellationToken.None);

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

        var result = await source.ObserveAsync(Target(), CancellationToken.None);

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

        var result = await source.ObserveAsync(Target(port: closedPort), CancellationToken.None);

        Assert.Equal("connection refused", result.Error);
    }

    [Theory]
    [InlineData("smtp")]
    [InlineData("imap")]
    [InlineData("pop3")]
    [InlineData("postgres")]
    public async Task Negotiates_starttls_before_reading_the_certificate(string scheme)
    {
        var notAfter = Dates.Now.AddDays(20);
        using var certificate = TestCertificates.Create(notAfter);
        var server = ServeAsync(certificate, Preambles[scheme]);

        var result = await source.ObserveAsync(Target(scheme), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Dates.AssertSameSecond(notAfter, Assert.Single(result.Value).ExpiresAt);
        await server;
    }

    [Fact]
    public async Task Reports_a_server_that_refuses_starttls()
    {
        using var certificate = TestCertificates.Create(Dates.Now.AddDays(20));
        var server = ServeAsync(certificate, async stream =>
        {
            await Send(stream, "220 mail.test ESMTP");
            await ReadLine(stream);
            await Send(stream, "250 mail.test");
            await ReadLine(stream);
            await Send(stream, "454 TLS not available");
            return false;
        });

        var result = await source.ObserveAsync(Target("smtp"), CancellationToken.None);

        Assert.Equal("the SMTP server refused STARTTLS: 454 TLS not available", result.Error);
        await server;
    }

    [Fact]
    public async Task Reports_a_postgres_server_without_tls()
    {
        using var certificate = TestCertificates.Create(Dates.Now.AddDays(20));
        var server = ServeAsync(certificate, async stream =>
        {
            await stream.ReadExactlyAsync(new byte[8]);
            await stream.WriteAsync("N"u8.ToArray());
            return false;
        });

        var result = await source.ObserveAsync(Target("postgres"), CancellationToken.None);

        Assert.Equal("the PostgreSQL server does not accept TLS", result.Error);
        await server;
    }

    public ValueTask DisposeAsync()
    {
        listener.Dispose();
        return ValueTask.CompletedTask;
    }

    private static readonly Dictionary<string, Func<Stream, Task<bool>>> Preambles = new()
    {
        ["smtp"] = async stream =>
        {
            await Send(stream, "220 mail.test ESMTP");
            await ReadLine(stream);
            await Send(stream, "250-mail.test\r\n250 STARTTLS");
            await ReadLine(stream);
            await Send(stream, "220 ready");
            return true;
        },
        ["imap"] = async stream =>
        {
            await Send(stream, "* OK ready");
            await ReadLine(stream);
            await Send(stream, "a1 OK begin TLS");
            return true;
        },
        ["pop3"] = async stream =>
        {
            await Send(stream, "+OK ready");
            await ReadLine(stream);
            await Send(stream, "+OK begin TLS");
            return true;
        },
        ["postgres"] = async stream =>
        {
            await stream.ReadExactlyAsync(new byte[8]);
            await stream.WriteAsync("S"u8.ToArray());
            return true;
        },
    };

    private int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    private WatchTarget Target(string? scheme = null, int? port = null)
    {
        var endpoint = $"127.0.0.1:{port ?? Port}";
        return new(new ItemKey(ItemKind.Tls, scheme is null ? endpoint : $"{scheme}://{endpoint}"), "it");
    }

    private Task ServeAsync(X509Certificate2 certificate, Func<Stream, Task<bool>>? preamble = null)
    {
        listener.Start();
        return Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            if (preamble is not null && !await preamble(stream))
            {
                return;
            }

            await using var tls = new SslStream(stream);
            try
            {
                await tls.AuthenticateAsServerAsync(certificate);
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException)
            {
            }
        });
    }

    private static Task Send(Stream stream, string lines) => stream.WriteAsync(Encoding.ASCII.GetBytes(lines + "\r\n")).AsTask();

    private static async Task ReadLine(Stream stream)
    {
        var buffer = new byte[1];
        while (await stream.ReadAsync(buffer) == 1 && buffer[0] != (byte)'\n')
        {
        }
    }
}
