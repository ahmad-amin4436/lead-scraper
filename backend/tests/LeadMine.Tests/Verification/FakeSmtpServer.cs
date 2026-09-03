using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LeadMine.Tests.Verification;

/// <summary>
/// A minimal, scripted SMTP server on an ephemeral loopback port, purely for
/// exercising <c>SmtpProbe</c>'s socket handling without touching a real mail
/// server (or needing port 25, which is privileged and usually already
/// claimed). Speaks just enough of the protocol to get through greeting/EHLO/
/// MAIL FROM, then replies to each RCPT TO with the next scripted line —
/// letting a test simulate a 2xx accept, a 4xx that clears on retry, a 5xx
/// reject, a connection that free-runs into a timeout, or a garbled reply.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Queue<string> _rcptResponses;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    private FakeSmtpServer(TcpListener listener, IEnumerable<string> rcptResponses)
    {
        _listener = listener;
        _rcptResponses = new Queue<string>(rcptResponses);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// Starts listening. <paramref name="rcptResponses"/> is consumed one
    /// line per RCPT TO received, across however many connections a test's
    /// probe makes (a retry opens a fresh connection, so the second scripted
    /// line is what the retry itself sees).
    /// </summary>
    public static FakeSmtpServer Start(params string[] rcptResponses)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var server = new FakeSmtpServer(listener, rcptResponses);
        server._acceptLoop = server.AcceptLoopAsync(server._cts.Token);
        return server;
    }

    /// <summary>A server that accepts the connection but never sends the initial greeting — simulates a hung/black-holed host.</summary>
    public static FakeSmtpServer StartSilent()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var server = new FakeSmtpServer(listener, []);
        server._acceptLoop = server.SilentAcceptLoopAsync(server._cts.Token);
        return server;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(ct);
                await HandleConnectionAsync(client, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — expected.
        }
        catch (ObjectDisposedException)
        {
            // Listener stopped mid-accept — expected on dispose.
        }
    }

    private async Task SilentAcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                // Never write anything, never close — the caller's own
                // ConnectAsync/first read is left to hang until its own
                // timeout fires. Kept alive for the test's duration by
                // simply never disposing it here.
                _ = client;
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

        await writer.WriteLineAsync("220 fake-smtp ready".AsMemory(), ct);

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) return;

            if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 fake-smtp".AsMemory(), ct);
            }
            else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 OK".AsMemory(), ct);
            }
            else if (line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
            {
                var response = _rcptResponses.Count > 0 ? _rcptResponses.Dequeue() : "250 OK";
                await writer.WriteLineAsync(response.AsMemory(), ct);
            }
            else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* already handled inside the loop */ }
        }

        _cts.Dispose();
    }
}
