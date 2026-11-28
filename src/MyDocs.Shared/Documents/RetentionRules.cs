namespace MyDocs.Shared.Documents;

/// <summary>Bezugspunkt einer Aufbewahrungsfrist (28, 174); Texte unter <c>RetentionBasis_{Wert}</c>.</summary>
public enum RetentionBasis
{
    /// <summary>Ab dem Dokumentdatum (ohne Datum: ab dem Eingang).</summary>
    DocumentDate,

    /// <summary>Ab dem Ende des Jahres des Dokumentdatums (Steuer, Kontoauszüge, Rechnungen).</summary>
    EndOfYear,

    /// <summary>Ab dem Ende der zugeordneten Einträge (Police, Vertrag, Kündigung); solange einer läuft, keine Frist.</summary>
    ObjectEnd,

    /// <summary>Dauerhaft aufbewahren.</summary>
    Forever,
}

/// <summary>Vorgeschlagene Aufbewahrung einer Dokumentart (28, 174). Nie automatisch löschen: Nach Ablauf entsteht „Aufräumen“.</summary>
public sealed record RetentionRule(string Kind, int Years, RetentionBasis Basis);

public static class RetentionRules
{
    /// <summary>Behalten ohne Frist (von Hand gesetzt).</summary>
    public static readonly DateOnly Forever = DateOnly.MaxValue;

    /// <summary>
    /// Vorschläge je Dokumentart (174), auf der sicheren Seite. „Sonstiges“ und Dokumente ohne Art haben keine Frist.
    /// </summary>
    public static IReadOnlyDictionary<string, RetentionRule> ByKind { get; } = new Dictionary<string, RetentionRule>(StringComparer.Ordinal)
    {
        ["invoice"] = new("invoice", 2, RetentionBasis.EndOfYear),
        ["receipt"] = new("receipt", 2, RetentionBasis.DocumentDate),
        ["letter"] = new("letter", 3, RetentionBasis.DocumentDate),
        ["contract"] = new("contract", 3, RetentionBasis.ObjectEnd),
        ["policy"] = new("policy", 3, RetentionBasis.ObjectEnd),
        ["cancellation"] = new("cancellation", 3, RetentionBasis.ObjectEnd),
        ["notice"] = new("notice", 10, RetentionBasis.EndOfYear),
        ["statement"] = new("statement", 10, RetentionBasis.EndOfYear),
        ["certificate"] = new("certificate", 0, RetentionBasis.Forever),
        ["payslip"] = new("payslip", 0, RetentionBasis.Forever),
    };

    public static RetentionRule? For(string? kind) => kind is not null && ByKind.TryGetValue(kind, out var rule) ? rule : null;
}

/// <summary>Ein Dokument, dessen Aufbewahrung abgelaufen ist (Seite „Aufräumen“).</summary>
public sealed record CleanupItemDto(Guid Id, string Title, string? Kind, DateOnly? DocumentDate, DateOnly RetainUntil, RetentionBasis? Basis, int Years,
    IReadOnlyList<Guid> ObjectIds, bool CanEdit);

/// <summary>Aufbewahrung von Hand: bis zu einem Tag, dauerhaft oder (beides leer) wieder nach der Regel.</summary>
public sealed record RetentionRequest(DateOnly? Until, bool Forever = false);
