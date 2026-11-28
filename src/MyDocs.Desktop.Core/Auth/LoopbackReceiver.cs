using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace MyDocs.Desktop.Core.Auth;

/// <summary>Die Freigabe wurde im Browser abgelehnt oder die Antwort passt nicht zur Anfrage.</summary>
public sealed class SignInFailedException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>
/// Empfängt den Code auf <c>http://127.0.0.1:&lt;Port&gt;/callback</c> (RFC 8252). Ein einfacher TCP-Empfänger statt
/// HttpListener, weil dieser unter Windows für 127.0.0.1 Administratorrechte verlangt. Nimmt genau eine Antwort an.
/// </summary>
public sealed class LoopbackReceiver : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public LoopbackReceiver()
    {
        _listener.Start();
        RedirectUri = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/callback";
    }

    public string RedirectUri { get; }

    /// <summary>Wartet auf den Aufruf mit passendem <paramref name="state"/> und liefert den Code.</summary>
    public async Task<string> WaitForCodeAsync(string state, string successPage, string failurePage, CancellationToken ct)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(ct) ?? string.Empty;
            // Kopfzeilen überlesen.
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct)))
            {
            }

            var parts = requestLine.Split(' ');
            if (parts.Length < 2 || parts[0] != "GET" || !parts[1].StartsWith("/callback", StringComparison.Ordinal))
            {
                // Etwa favicon.ico: nicht unser Aufruf.
                await RespondAsync(stream, 404, "Not found", ct);
                continue;
            }

            var query = HttpUtility.ParseQueryString(parts[1].Contains('?', StringComparison.Ordinal) ? parts[1][(parts[1].IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty);
            if (query["state"] != state)
            {
                await RespondAsync(stream, 400, failurePage, ct);
                throw new SignInFailedException("state");
            }

            if (query["error"] is { } error)
            {
                await RespondAsync(stream, 200, failurePage, ct);
                throw new SignInFailedException(error);
            }

            if (query["code"] is not { Length: > 0 } code)
            {
                await RespondAsync(stream, 400, failurePage, ct);
                throw new SignInFailedException("code");
            }

            await RespondAsync(stream, 200, successPage, ct);
            return code;
        }
    }

    private static async Task RespondAsync(Stream stream, int status, string body, CancellationToken ct)
    {
        var html = $"<!DOCTYPE html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>myDocs</title></head><body style=\"font-family:sans-serif;margin:3rem\"><p>{WebUtility.HtmlEncode(body)}</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(bytes, ct);
    }

    public void Dispose() => _listener.Dispose();
}
