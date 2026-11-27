using Microsoft.JSInterop;

namespace MyDocs.UI.Services;

/// <summary>Passkeys im Browser über <c>_content/MyDocs.UI/js/mydocs.webauthn.js</c> (navigator.credentials), wie myLife.</summary>
public sealed class WebAuthnService(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            return await (await ModuleAsync()).InvokeAsync<bool>("isSupported");
        }
        catch (JSException)
        {
            return false;
        }
    }

    /// <summary>Registriert einen neuen Passkey; liefert die Antwort des Authenticators als JSON oder null bei Abbruch.</summary>
    public Task<string?> CreateAsync(string optionsJson) => CallAsync("create", optionsJson);

    /// <summary>Bestätigt die Anmeldung mit einem Passkey; liefert die Antwort als JSON oder null bei Abbruch.</summary>
    public Task<string?> GetAsync(string optionsJson) => CallAsync("get", optionsJson);

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Seite wird geschlossen.
            }
        }
    }

    private async Task<string?> CallAsync(string function, string optionsJson)
    {
        try
        {
            return await (await ModuleAsync()).InvokeAsync<string>(function, optionsJson);
        }
        catch (JSException)
        {
            // Abbruch durch den Nutzer (NotAllowedError) oder kein passender Authenticator.
            return null;
        }
    }

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/MyDocs.UI/js/mydocs.webauthn.js");
}
