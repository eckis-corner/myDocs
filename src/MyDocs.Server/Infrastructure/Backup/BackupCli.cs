using MyDocs.Server.Infrastructure.Files;

namespace MyDocs.Server.Infrastructure.Backup;

/// <summary>
/// Kommandozeile für den Notfall (docs/betrieb/installation.md), bei gestoppter App:
/// <list type="bullet">
/// <item><c>restore &lt;Sicherung&gt; --confirm</c>: Datenbank, Dateien und Schlüssel aus einer Sicherung ersetzen.</item>
/// <item><c>master-key restore "&lt;24 Wörter&gt;" --confirm</c>: Hauptschlüssel aus dem Ausdruck zurückschreiben.</item>
/// </list>
/// Ohne <c>--confirm</c> passiert nichts.
/// </summary>
internal static class BackupCli
{
    public static bool Handles(string[] args) => args is ["restore", ..] or ["master-key", ..];

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, MyDocsOptions options)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        if (!args.Contains("--confirm", StringComparer.Ordinal))
        {
            await Console.Error.WriteLineAsync("Dieser Befehl ersetzt Daten. Zum Ausführen --confirm anhängen.");
            return 2;
        }

        return args[0] == "restore" ? await RestoreAsync(args, configuration, options) : await MasterKeyAsync(args, options);
    }

    private static async Task<int> RestoreAsync(string[] args, IConfiguration configuration, MyDocsOptions options)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            await Console.Error.WriteLineAsync("Aufruf: restore <Sicherungsdatei> --confirm");
            return 2;
        }

        var key = BackupFormat.ParseKey(options.BackupKey);
        var connectionString = configuration.GetConnectionString("MyDocs");
        if (key is null || string.IsNullOrWhiteSpace(connectionString))
        {
            await Console.Error.WriteLineAsync("MYDOCS_BACKUP_KEY oder die Datenbankverbindung fehlt.");
            return 2;
        }

        try
        {
            var manifest = await BackupArchive.RestoreAsync(args[1], connectionString, Path.GetFullPath(options.DataDirectory), key, CancellationToken.None);
            await Console.Out.WriteLineAsync(
                $"Zurückgespielt: Sicherung vom {manifest.CreatedAt:u}, {manifest.Tables.Count} Tabellen, {manifest.Documents} Dateien, {manifest.Keys} Schlüssel.");
            return 0;
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Security.Cryptography.CryptographicException or IOException
                                              or Npgsql.NpgsqlException or EndOfStreamException)
        {
            await Console.Error.WriteLineAsync($"Rücksicherung fehlgeschlagen: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> MasterKeyAsync(string[] args, MyDocsOptions options)
    {
        if (args is not [_, "restore", var words, ..] || words.StartsWith("--", StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync("Aufruf: master-key restore \"wort1 wort2 … wort24\" --confirm");
            return 2;
        }

        if (!MasterKey.Restore(options.DataDirectory, words))
        {
            await Console.Error.WriteLineAsync("Die Wörter ergeben keinen gültigen Schlüssel (Tippfehler oder fehlendes Wort?).");
            return 1;
        }

        await Console.Out.WriteLineAsync("Hauptschlüssel geschrieben nach keys/master.key.");
        return 0;
    }
}
