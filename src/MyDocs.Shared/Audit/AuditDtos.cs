namespace MyDocs.Shared.Audit;

/// <summary>Aktionen im Audit-Log (35); Texte unter <c>Audit_{Aktion}</c>.</summary>
public static class AuditActions
{
    public const string Login = "login";
    public const string LoginFailed = "login-failed";
    public const string Logout = "logout";
    public const string SetupCompleted = "setup-completed";
    public const string Activated = "activated";
    public const string UserCreated = "user-created";
    public const string UserChanged = "user-changed";
    public const string UserLocked = "user-locked";
    public const string UserUnlocked = "user-unlocked";
    public const string UserReset = "user-reset";
    public const string ObjectCreated = "object-created";
    public const string ObjectChanged = "object-changed";
    public const string ObjectDeleted = "object-deleted";
    public const string ObjectRestored = "object-restored";
    public const string ObjectShared = "object-shared";
    public const string ObjectViewed = "object-viewed";
    public const string DocumentUploaded = "document-uploaded";
    public const string DocumentVersionAdded = "document-version-added";
    public const string DocumentDownloaded = "document-downloaded";
    public const string DocumentDeleted = "document-deleted";
    public const string DocumentRestored = "document-restored";
    public const string DocumentAssigned = "document-assigned";
    public const string AdminView = "admin-view";
    public const string TrashPurged = "trash-purged";
    public const string BackupCreated = "backup-created";
    public const string BackupFailed = "backup-failed";
    public const string MasterKeyShown = "master-key-shown";
    public const string PasskeyAdded = "passkey-added";
    public const string PasskeyRemoved = "passkey-removed";
    public const string TaskCreated = "task-created";
    public const string TaskClosed = "task-closed";
    public const string SettingsChanged = "settings-changed";
    public const string MailFailed = "mail-failed";
}

/// <summary>Ein Eintrag im Audit-Log. Details ohne Inhalte, höchstens Titel.</summary>
public sealed record AuditEntryDto(long Id, DateTimeOffset At, string? ActorName, string Action, Guid? ObjectId, Guid? DocumentId, string? Title, string? Ip, string? Details);

/// <summary>Header, mit dem der Client die Admin-Einsicht einschaltet (31). Ohne die Rolle Administrator wirkungslos.</summary>
public static class AdminViewHeader
{
    public const string Name = "X-MyDocs-Admin-View";
    public const string Value = "1";
}
