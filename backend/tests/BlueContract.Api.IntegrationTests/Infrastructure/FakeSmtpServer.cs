using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BlueContract.Api.IntegrationTests.Infrastructure;

/// <summary>Minimal in-process SMTP server (no TLS, AUTH PLAIN/LOGIN accepted) that keeps the raw messages it receives.</summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public FakeSmtpServer()
    {
        _listener.Start();
        _loop = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> Messages { get; } = [];

    public List<string> Logins { get; } = [];

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // stopped
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 fake-smtp ready");
        while (await reader.ReadLineAsync() is { } line)
        {
            var command = line.Split(' ')[0].ToUpperInvariant();
            switch (command)
            {
                case "EHLO":
                    await writer.WriteLineAsync("250-fake-smtp");
                    await writer.WriteLineAsync("250-AUTH PLAIN LOGIN");
                    await writer.WriteLineAsync("250 8BITMIME");
                    break;
                case "AUTH":
                    var parts = line.Split(' ');
                    if (parts.Length >= 3 && parts[1].Equals("PLAIN", StringComparison.OrdinalIgnoreCase))
                    {
                        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])).Split('\0');
                        lock (Logins)
                        {
                            Logins.Add($"{decoded[1]}:{decoded[2]}");
                        }

                        await writer.WriteLineAsync("235 ok");
                    }
                    else
                    {
                        await writer.WriteLineAsync("334 VXNlcm5hbWU6");
                        var user = Encoding.UTF8.GetString(Convert.FromBase64String(await reader.ReadLineAsync() ?? string.Empty));
                        await writer.WriteLineAsync("334 UGFzc3dvcmQ6");
                        var password = Encoding.UTF8.GetString(Convert.FromBase64String(await reader.ReadLineAsync() ?? string.Empty));
                        lock (Logins)
                        {
                            Logins.Add($"{user}:{password}");
                        }

                        await writer.WriteLineAsync("235 ok");
                    }

                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 end with .");
                    var body = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } data && data != ".")
                    {
                        body.AppendLine(data);
                    }

                    lock (Messages)
                    {
                        Messages.Add(body.ToString());
                    }

                    await writer.WriteLineAsync("250 queued");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 bye");
                    return;
                default:
                    await writer.WriteLineAsync("250 ok");
                    break;
            }
        }
    }
}
