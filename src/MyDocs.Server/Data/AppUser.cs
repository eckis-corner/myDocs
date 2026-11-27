using Microsoft.AspNetCore.Identity;

namespace MyDocs.Server.Data;

/// <summary>Benutzer mit Login (4). Nur der Administrator legt Benutzer an; der erste entsteht im Einrichtungsassistenten.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Letzter angenommener TOTP-Zeitschritt; jeder Code gilt nur einmal.</summary>
    public long LastTotpStep { get; set; }
}

public static class Roles
{
    public const string Admin = "admin";
}
