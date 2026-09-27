using System.Text;
using Lapse.Core;
using Lapse.Infrastructure.Sources;

namespace Lapse.Infrastructure.Certificates;

internal static class StartTls
{
    private const int MaxLineLength = 4096;

    private static readonly byte[] PostgresSslRequest = [0, 0, 0, 8, 0x04, 0xD2, 0x16, 0x2F];

    public static Task<Result<Unit>> NegotiateAsync(Stream stream, TlsProtocol protocol, CancellationToken cancellationToken) => protocol switch
    {
        TlsProtocol.Direct => Task.FromResult(Result.Success(Unit.Value)),
        TlsProtocol.Smtp => SmtpAsync(stream, cancellationToken),
        TlsProtocol.Imap => ImapAsync(stream, cancellationToken),
        TlsProtocol.Pop3 => Pop3Async(stream, cancellationToken),
        TlsProtocol.Postgres => PostgresAsync(stream, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, null),
    };

    private static async Task<Result<Unit>> SmtpAsync(Stream stream, CancellationToken cancellationToken)
    {
        var steps = new (string? Command, string Expected)[] { (null, "220"), ("EHLO lapse", "250"), ("STARTTLS", "220") };
        foreach (var (command, expected) in steps)
        {
            if (command is not null)
            {
                await WriteLineAsync(stream, command, cancellationToken);
            }

            var reply = await ReadSmtpReplyAsync(stream, cancellationToken);
            if (reply is null || !reply.StartsWith(expected, StringComparison.Ordinal))
            {
                return Refused("SMTP", reply);
            }
        }

        return Result.Success(Unit.Value);
    }

    private static async Task<Result<Unit>> ImapAsync(Stream stream, CancellationToken cancellationToken)
    {
        var greeting = await ReadLineAsync(stream, cancellationToken);
        if (greeting is null || !greeting.StartsWith("* OK", StringComparison.Ordinal))
        {
            return Refused("IMAP", greeting);
        }

        await WriteLineAsync(stream, "a1 STARTTLS", cancellationToken);
        string? reply;
        do
        {
            reply = await ReadLineAsync(stream, cancellationToken);
        }
        while (reply is not null && !reply.StartsWith("a1 ", StringComparison.Ordinal));

        return reply?.StartsWith("a1 OK", StringComparison.Ordinal) == true ? Result.Success(Unit.Value) : Refused("IMAP", reply);
    }

    private static async Task<Result<Unit>> Pop3Async(Stream stream, CancellationToken cancellationToken)
    {
        foreach (var command in new string?[] { null, "STLS" })
        {
            if (command is not null)
            {
                await WriteLineAsync(stream, command, cancellationToken);
            }

            var reply = await ReadLineAsync(stream, cancellationToken);
            if (reply is null || !reply.StartsWith("+OK", StringComparison.Ordinal))
            {
                return Refused("POP3", reply);
            }
        }

        return Result.Success(Unit.Value);
    }

    private static async Task<Result<Unit>> PostgresAsync(Stream stream, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(PostgresSslRequest, cancellationToken);
        var answer = new byte[1];
        var read = await stream.ReadAsync(answer, cancellationToken);
        return read == 1 && answer[0] == (byte)'S'
            ? Result.Success(Unit.Value)
            : Result.Failure<Unit>("the PostgreSQL server does not accept TLS");
    }

    private static async Task<string?> ReadSmtpReplyAsync(Stream stream, CancellationToken cancellationToken)
    {
        string? line;
        do
        {
            line = await ReadLineAsync(stream, cancellationToken);
        }
        while (line is { Length: > 3 } && line[3] == '-');

        return line;
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = new List<byte>();
        var buffer = new byte[1];
        while (line.Count < MaxLineLength && await stream.ReadAsync(buffer, cancellationToken) == 1)
        {
            if (buffer[0] == (byte)'\n')
            {
                return Encoding.ASCII.GetString([.. line]).TrimEnd('\r');
            }

            line.Add(buffer[0]);
        }

        return null;
    }

    private static Task WriteLineAsync(Stream stream, string command, CancellationToken cancellationToken) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(command + "\r\n"), cancellationToken).AsTask();

    private static Result<Unit> Refused(string protocol, string? reply) => Result.Failure<Unit>(reply is null
        ? $"the {protocol} server closed the connection before STARTTLS"
        : $"the {protocol} server refused STARTTLS: {reply}");
}
