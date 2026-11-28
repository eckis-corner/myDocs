using System.Globalization;
using System.Text.RegularExpressions;

namespace MyDocs.Shared.Objects;

/// <summary>
/// Prüfregeln je Typ (13; Werte aus objekttypen.md, Abschnitt 5): Prüfintervall und Fristen aus Feldern. Jedes Objekt
/// kann Intervall und nächsten Termin überschreiben.
/// </summary>
public static partial class ReviewRules
{
    /// <summary>So viele Tage darf ein Dokument im Posteingang liegen, bis er fällig wird (20, 143).</summary>
    public const int InboxDueDays = 7;

    /// <summary>Frist aus Feldern: Bezugsdatum, Vorlauf der Erinnerung und Textschlüssel des Vorgangs.</summary>
    public sealed record Deadline(string Key, DateOnly Date, int LeadDays);

    private static readonly Dictionary<string, int> IntervalMonths = new(StringComparer.Ordinal)
    {
        ["insurance"] = 12,
        ["contract"] = 6,
        ["loan"] = 12,
        ["bankAccount"] = 12,
        ["depot"] = 12,
        ["legalDocument"] = 24,
    };

    /// <summary>Vorlauf vor dem Ablaufdatum, ab dem ein gültiger Eintrag „läuft ab“ (3 Monate).</summary>
    public const int ExpiringLeadDays = 91;

    /// <summary>Höchstens so weit lässt sich eine Prüfung oder Frist zurückstellen (16).</summary>
    public const int MaxPostponeDays = 30;

    public static int? DefaultIntervalMonths(string typeKey) => IntervalMonths.TryGetValue(typeKey, out var months) ? months : null;

    /// <summary>
    /// Frist aus den Feldern, frühestens heute: Versicherung (Hauptfälligkeit minus Kündigungsfrist, 6 Wochen Vorlauf),
    /// Vertrag (Ablaufdatum minus Kündigungsfrist, 4 Wochen), Kredit (Ablaufdatum, 6 Monate). Null, wenn die Felder
    /// fehlen oder sich nicht lesen lassen.
    /// </summary>
    public static Deadline? DeadlineFor(string typeKey, ObjectData data, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(data);
        switch (typeKey)
        {
            case "insurance" when NextDueDate(data.Get("mainDueDate"), today) is { } due:
                var notice = ParsePeriod(data.Get("noticePeriod"));
                return new Deadline("cancellation-deadline", Subtract(due, notice), 42);
            case "contract" when data.ExpiresOn is { } end:
                return new Deadline("cancellation-deadline", Subtract(end, ParsePeriod(data.Get("noticePeriod"))), 28);
            case "loan" when data.ExpiresOn is { } end:
                return new Deadline("loan-end", end, 182);
            default:
                return null;
        }
    }

    /// <summary>Nächster Termin einer jährlichen Hauptfälligkeit („01.01.“, „1.1.2027“, „1. Januar“, „2027-01-01“) ab heute.</summary>
    public static DateOnly? NextDueDate(string? text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (DateOnly.TryParseExact(value, ["yyyy-MM-dd", "d.M.yyyy", "dd.MM.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return Roll(exact.Month, exact.Day, today);
        }

        var numeric = DayMonth().Match(value);
        if (numeric.Success)
        {
            return Roll(int.Parse(numeric.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(numeric.Groups[1].Value, CultureInfo.InvariantCulture), today);
        }

        var named = DayMonthName().Match(value);
        if (named.Success && MonthOf(named.Groups[2].Value) is { } month)
        {
            return Roll(month, int.Parse(named.Groups[1].Value, CultureInfo.InvariantCulture), today);
        }

        return null;
    }

    /// <summary>Kündigungsfrist als Zeitspanne: „3 Monate“, „1 Monat“, „6 Wochen“, „30 Tage“, „3 months“. Unbekannt: 0.</summary>
    public static (int Months, int Days) ParsePeriod(string? text)
    {
        var match = Period().Match(text ?? string.Empty);
        if (!match.Success)
        {
            return (0, 0);
        }

        var amount = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups[2].Value.ToLowerInvariant();
        return unit.StartsWith('m') ? (amount, 0) : unit.StartsWith('w') ? (0, amount * 7) : (0, amount);
    }

    private static DateOnly Subtract(DateOnly date, (int Months, int Days) period) => date.AddMonths(-period.Months).AddDays(-period.Days);

    private static DateOnly? Roll(int month, int day, DateOnly today)
    {
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(today.Year, month))
        {
            return null;
        }

        var candidate = new DateOnly(today.Year, month, day);
        return candidate < today ? candidate.AddYears(1) : candidate;
    }

    private static int? MonthOf(string name)
    {
        var lower = name.ToLowerInvariant();
        string[] german = ["jan", "feb", "mär", "apr", "mai", "jun", "jul", "aug", "sep", "okt", "nov", "dez"];
        string[] english = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
        for (var i = 0; i < 12; i++)
        {
            if (lower.StartsWith(german[i], StringComparison.Ordinal) || lower.StartsWith(english[i], StringComparison.Ordinal) || (i == 2 && lower.StartsWith("maer", StringComparison.Ordinal)))
            {
                return i + 1;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^(\d{1,2})\.\s*(\d{1,2})\.?$")]
    private static partial Regex DayMonth();

    [GeneratedRegex(@"^(\d{1,2})\.?\s*([A-Za-zÄÖÜäöü]+)")]
    private static partial Regex DayMonthName();

    [GeneratedRegex(@"(\d+)\s*(Monat|Monate|Month|Months|Woche|Wochen|Week|Weeks|Tag|Tage|Day|Days)", RegexOptions.IgnoreCase)]
    private static partial Regex Period();
}

/// <summary>Stand der Prüfung eines Objekts für die Objektseite und das Dashboard.</summary>
public sealed record ReviewInfoDto(int? IntervalMonths, bool IntervalOverridden, DateOnly? NextReviewOn, bool NextReviewOverridden, DateOnly? LastReviewedOn,
    DateOnly? OverdueSince, DateOnly? DeadlineOn, string? DeadlineKey);

/// <summary>Eigene Prüfregel eines Objekts: Intervall in Monaten (0 = keine regelmäßige Prüfung) und fester nächster Termin.</summary>
public sealed record ReviewSettingsRequest(int? IntervalMonths, DateOnly? NextReviewOn);
