namespace MyDocs.Shared.Objects;

/// <summary>Muster des Lebenszyklus (9). Jeder Typ wählt eines; Typen ohne Muster haben keinen Lebenszyklus.</summary>
public enum LifecyclePattern
{
    None,

    /// <summary>in Abschluss → aktiv → gekündigt (läuft bis …) → beendet → archiviert.</summary>
    Contract,

    /// <summary>gültig → läuft ab → abgelaufen → ersetzt → archiviert; „läuft ab“ und „abgelaufen“ setzt das System.</summary>
    Validity,

    /// <summary>vorhanden → abgegeben/verkauft → archiviert.</summary>
    Asset,
}

/// <summary>Zustände aller Muster. Texte unter <c>Lifecycle_{Zustand}</c>. Werte landen in der Datenbank: nur ergänzen.</summary>
public static class LifecycleStates
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Cancelled = "cancelled";
    public const string Ended = "ended";
    public const string Valid = "valid";
    public const string Expiring = "expiring";
    public const string Expired = "expired";
    public const string Replaced = "replaced";
    public const string Present = "present";
    public const string Disposed = "disposed";
    public const string Archived = "archived";

    /// <summary>Zustände, die nur der Wiedervorlage-Dienst setzt (aus dem Ablaufdatum).</summary>
    public static IReadOnlySet<string> Automatic { get; } = new HashSet<string>(StringComparer.Ordinal) { Expiring, Expired };

    /// <summary>Zustände, in denen ein Objekt nicht mehr läuft: keine Wiedervorlage, keine Zahlungen.</summary>
    public static IReadOnlySet<string> Inactive { get; } = new HashSet<string>(StringComparer.Ordinal) { Ended, Replaced, Disposed, Archived };
}

/// <summary>Ein Wechsel im Lebenszyklus, den eine Person auslöst: Zustand, Datum (etwa „gekündigt zum“) und Nachfolger.</summary>
public sealed record LifecycleChangeRequest(string State, DateOnly? Date, Guid? ReplacedById, uint Version);

/// <summary>Lebenszyklus je Muster: Anfangszustand und erlaubte Wechsel von Hand (9).</summary>
public static class Lifecycle
{
    private static readonly Dictionary<LifecyclePattern, (string Initial, Dictionary<string, string[]> Next)> Patterns = new()
    {
        [LifecyclePattern.Contract] = (LifecycleStates.Active, new()
        {
            [LifecycleStates.Draft] = [LifecycleStates.Active, LifecycleStates.Archived],
            [LifecycleStates.Active] = [LifecycleStates.Cancelled, LifecycleStates.Ended],
            [LifecycleStates.Cancelled] = [LifecycleStates.Active, LifecycleStates.Ended],
            [LifecycleStates.Ended] = [LifecycleStates.Archived, LifecycleStates.Active],
            [LifecycleStates.Archived] = [LifecycleStates.Ended],
        }),
        [LifecyclePattern.Validity] = (LifecycleStates.Valid, new()
        {
            [LifecycleStates.Valid] = [LifecycleStates.Replaced, LifecycleStates.Archived],
            [LifecycleStates.Expiring] = [LifecycleStates.Replaced, LifecycleStates.Archived],
            [LifecycleStates.Expired] = [LifecycleStates.Replaced, LifecycleStates.Archived],
            [LifecycleStates.Replaced] = [LifecycleStates.Archived],
            [LifecycleStates.Archived] = [LifecycleStates.Valid],
        }),
        [LifecyclePattern.Asset] = (LifecycleStates.Present, new()
        {
            [LifecycleStates.Present] = [LifecycleStates.Disposed],
            [LifecycleStates.Disposed] = [LifecycleStates.Archived, LifecycleStates.Present],
            [LifecycleStates.Archived] = [LifecycleStates.Disposed],
        }),
    };

    /// <summary>Muster je Typ (objekttypen.md, Abschnitt 3); Typen ohne Eintrag haben keinen Lebenszyklus.</summary>
    private static readonly Dictionary<string, LifecyclePattern> ByType = new(StringComparer.Ordinal)
    {
        ["bankAccount"] = LifecyclePattern.Contract,
        ["depot"] = LifecyclePattern.Contract,
        ["safeDeposit"] = LifecyclePattern.Contract,
        ["loan"] = LifecyclePattern.Contract,
        ["insurance"] = LifecyclePattern.Contract,
        ["contract"] = LifecyclePattern.Contract,
        ["domain"] = LifecyclePattern.Contract,
        ["employment"] = LifecyclePattern.Contract,
        ["pension"] = LifecyclePattern.Contract,
        ["idDocument"] = LifecyclePattern.Validity,
        ["card"] = LifecyclePattern.Validity,
        ["legalDocument"] = LifecyclePattern.Validity,
        ["property"] = LifecyclePattern.Asset,
        ["vehicle"] = LifecyclePattern.Asset,
        ["asset"] = LifecyclePattern.Asset,
        ["onlineAccount"] = LifecyclePattern.Asset,
        ["cryptoWallet"] = LifecyclePattern.Asset,
        ["device"] = LifecyclePattern.Asset,
    };

    public static LifecyclePattern PatternOf(string typeKey) => ByType.GetValueOrDefault(typeKey, LifecyclePattern.None);

    public static string? InitialState(string typeKey) => Patterns.TryGetValue(PatternOf(typeKey), out var p) ? p.Initial : null;

    public static IReadOnlyList<string> StatesOf(string typeKey) =>
        Patterns.TryGetValue(PatternOf(typeKey), out var p) ? [.. p.Next.Keys] : [];

    /// <summary>Wechsel, die eine Person von <paramref name="state"/> aus auslösen darf.</summary>
    public static IReadOnlyList<string> NextStates(string typeKey, string? state) =>
        state is not null && Patterns.TryGetValue(PatternOf(typeKey), out var p) && p.Next.TryGetValue(state, out var next) ? next : [];

    public static bool CanChange(string typeKey, string? from, string to) => NextStates(typeKey, from).Contains(to);

    /// <summary>Braucht der Zustand ein Datum? „gekündigt zum“, „beendet am“, „abgegeben am“.</summary>
    public static bool NeedsDate(string state) => state is LifecycleStates.Cancelled or LifecycleStates.Ended or LifecycleStates.Disposed;
}
