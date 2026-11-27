using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;

namespace MyDocs.Server.Infrastructure;

/// <summary>
/// Einrichtungscode für den ersten Start. Solange es keinen Nutzer gibt, darf nur einrichten, wer den Code aus dem
/// Container-Protokoll kennt; sonst würde Administrator, wer die offene URL zuerst aufruft. Der Code liegt nur im
/// Arbeitsspeicher und entsteht bei jedem Start neu. Ausnahme von der Regel „keine Tokens im Log“: Das Protokoll
/// ist der einzige Weg zum Betreiber, und der Code verfällt mit der Einrichtung.
/// </summary>
public sealed partial class SetupTokenService(ILogger<SetupTokenService> logger)
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly Lock _lock = new();
    private byte[]? _token;

    internal string? CurrentToken { get; private set; }

    public void Issue()
    {
        var characters = RandomNumberGenerator.GetItems<char>(Alphabet, 20);
        var token = string.Join('-', characters.Chunk(4).Select(chunk => new string(chunk)));
        lock (_lock)
        {
            CurrentToken = token;
            _token = Normalize(token);
        }

        LogTokenIssued(logger, token);
    }

    public bool Verify(string? token)
    {
        lock (_lock)
        {
            return _token is not null && token is not null && CryptographicOperations.FixedTimeEquals(_token, Normalize(token));
        }
    }

    public void Revoke()
    {
        lock (_lock)
        {
            _token = null;
            CurrentToken = null;
        }
    }

    private static byte[] Normalize(string token) =>
        Encoding.ASCII.GetBytes(new string([.. token.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit)]));

    [LoggerMessage(Level = LogLevel.Warning, Message = "myDocs ist noch nicht eingerichtet. Einrichtungscode für den Assistenten: {SetupToken}")]
    private static partial void LogTokenIssued(ILogger logger, string setupToken);
}

/// <summary>Stellt beim Start einen Einrichtungscode aus, solange myDocs nicht eingerichtet ist.</summary>
internal sealed class SetupTokenIssuer(IServiceProvider services, SetupTokenService tokens) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
        // Eingerichtet ist myDocs erst mit einem Administrator, der den zweiten Faktor bestätigt hat.
        if (!await db.Users.AnyAsync(u => u.TwoFactorEnabled, cancellationToken))
        {
            tokens.Issue();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
