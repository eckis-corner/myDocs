namespace MyDocs.Shared.Payments;

/// <summary>
/// Eine regelmäßige Zahlung aus einem Eintrag (62, 173): Betrag und Rhythmus wie eingetragen, dazu das belastete Konto,
/// wer zuständig ist und bei gekündigten Einträgen das Ende.
/// </summary>
public sealed record PaymentDto(
    Guid ObjectId,
    string Title,
    string TypeKey,
    int Area,
    decimal Amount,
    string? Frequency,
    Guid? AccountId,
    string? AccountTitle,
    Guid? ResponsiblePersonId,
    string? ResponsibleName,
    bool Household,
    DateOnly? EndsOn)
{
    /// <summary>Zahlungen je Jahr; null bei „einmalig“ und ohne Rhythmus.</summary>
    public int? TimesPerYear => PaymentMath.TimesPerYear(Frequency);

    public decimal? Yearly => TimesPerYear * Amount;

    public decimal? Monthly => Yearly / 12;

    /// <summary>Gekündigt: läuft bis <see cref="EndsOn"/>.</summary>
    public bool Cancelled => EndsOn is not null;
}

public static class PaymentMath
{
    public static int? TimesPerYear(string? frequency) => frequency switch
    {
        "monthly" => 12,
        "quarterly" => 4,
        "halfYearly" => 2,
        "yearly" => 1,
        _ => null,
    };

    /// <summary>Summe je Monat, aus der Jahressumme gerechnet, damit sich Rundungen nicht addieren.</summary>
    public static decimal Monthly(IEnumerable<PaymentDto> payments) => payments.Sum(p => p.Yearly ?? 0) / 12;

    public static decimal Yearly(IEnumerable<PaymentDto> payments) => payments.Sum(p => p.Yearly ?? 0);
}
