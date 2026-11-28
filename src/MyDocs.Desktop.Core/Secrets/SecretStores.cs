using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MyDocs.Desktop.Core.Secrets;

/// <summary>Schlüsselspeicher des Betriebssystems (41): Credential Manager, Keychain, libsecret.</summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string key, CancellationToken ct);

    Task SetAsync(string key, string secret, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>Kein Schlüsselspeicher erreichbar (etwa Linux ohne secret-tool).</summary>
public sealed class SecretStoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public static class SecretStores
{
    public const string Service = "myDocs";

    public static ISecretStore ForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? new WindowsCredentialStore()
        : OperatingSystem.IsMacOS() ? new MacKeychainStore()
        : new LinuxSecretToolStore();

    internal static async Task<(int Exit, string Output)> RunAsync(string tool, IEnumerable<string> arguments, string? input, CancellationToken ct)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception exception)
        {
            throw new SecretStoreUnavailableException($"{tool} ist nicht installiert.", exception);
        }

        using (process)
        {
            if (process is null)
            {
                throw new SecretStoreUnavailableException($"{tool} startet nicht.");
            }

            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input);
                process.StandardInput.Close();
            }

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return (process.ExitCode, output.TrimEnd('\n', '\r'));
        }
    }
}

/// <summary>macOS: Schlüsselbund über <c>security</c>.</summary>
public sealed class MacKeychainStore : ISecretStore
{
    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var (exit, output) = await SecretStores.RunAsync("security", ["find-generic-password", "-s", SecretStores.Service, "-a", key, "-w"], null, ct);
        return exit == 0 ? output : null;
    }

    public async Task SetAsync(string key, string secret, CancellationToken ct)
    {
        // -U ersetzt einen vorhandenen Eintrag. Das Geheimnis steht kurz in der Befehlszeile (nur für die eigene Person sichtbar).
        var (exit, _) = await SecretStores.RunAsync("security", ["add-generic-password", "-U", "-s", SecretStores.Service, "-a", key, "-w", secret], null, ct);
        if (exit != 0)
        {
            throw new SecretStoreUnavailableException("Der Schlüsselbund hat das Token nicht gespeichert.");
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct) =>
        SecretStores.RunAsync("security", ["delete-generic-password", "-s", SecretStores.Service, "-a", key], null, ct);
}

/// <summary>Linux: Secret Service (GNOME Keyring, KWallet) über <c>secret-tool</c>; das Geheimnis geht über stdin.</summary>
public sealed class LinuxSecretToolStore : ISecretStore
{
    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var (exit, output) = await SecretStores.RunAsync("secret-tool", ["lookup", "service", SecretStores.Service, "account", key], null, ct);
        return exit == 0 && output.Length > 0 ? output : null;
    }

    public async Task SetAsync(string key, string secret, CancellationToken ct)
    {
        var (exit, _) = await SecretStores.RunAsync("secret-tool", ["store", "--label=myDocs", "service", SecretStores.Service, "account", key], secret, ct);
        if (exit != 0)
        {
            throw new SecretStoreUnavailableException("Der Schlüsselspeicher (secret-tool) hat das Token nicht gespeichert.");
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct) =>
        SecretStores.RunAsync("secret-tool", ["clear", "service", SecretStores.Service, "account", key], null, ct);
}

/// <summary>Windows: Anmeldeinformationsverwaltung (CredWrite/CredRead), Typ „generisch“, nur für dieses Konto.</summary>
public sealed partial class WindowsCredentialStore : ISecretStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public Task<string?> GetAsync(string key, CancellationToken ct)
    {
        if (!CredRead(Target(key), CredTypeGeneric, 0, out var pointer))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task SetAsync(string key, string secret, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        var target = Marshal.StringToHGlobalUni(Target(key));
        var user = Marshal.StringToHGlobalUni(key);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = user,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new SecretStoreUnavailableException($"Die Anmeldeinformationsverwaltung hat das Token nicht gespeichert ({Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(target);
            Marshal.FreeHGlobal(user);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        CredDelete(Target(key), CredTypeGeneric, 0);
        return Task.CompletedTask;
    }

    private static string Target(string key) => $"{SecretStores.Service}:{key}";

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, int type, int flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(ref Credential credential, int flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, int type, int flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr buffer);
}
