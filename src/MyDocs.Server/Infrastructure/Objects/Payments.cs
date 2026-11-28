using MyCommon.Catalog;
using MyDocs.Server.Data;
using MyDocs.Shared.Objects;

namespace MyDocs.Server.Infrastructure.Objects;

/// <summary>Regelmäßige Zahlung eines Eintrags nach dem Katalog (<see cref="ObjectTypeDefinition.Payment"/>), wie in myLife (142).</summary>
internal static class Payments
{
    public sealed record Payment(decimal Amount, string? Frequency, Guid? AccountId);

    /// <summary>
    /// Die Zahlung oder null: ohne Zahlung im Typ, ohne Betrag, wenn die Bedingung nicht passt (Kredit, der mir geschuldet
    /// wird), im Entwurf und wenn der Eintrag nicht mehr läuft (beendet, abgelaufen, ersetzt, abgegeben, archiviert).
    /// </summary>
    public static Payment? Of(DocObject item)
    {
        if (item.IsDeleted || ObjectCatalog.Find(item.TypeKey)?.Payment is not { } definition
            || item.LifecycleState is LifecycleStates.Draft or LifecycleStates.Expired || (item.LifecycleState is { } state && LifecycleStates.Inactive.Contains(state)))
        {
            return null;
        }

        var values = new ObjectData { Fields = item.Fields };
        if (definition.When is { } condition && !condition.Values.Contains(values.Get(condition.FieldKey)))
        {
            return null;
        }

        if (!FieldValues.TryNumber(values.Get(definition.AmountField), out var amount) || amount <= 0)
        {
            return null;
        }

        var frequency = definition.FixedFrequency ?? (definition.FrequencyField is { } field ? values.Get(field) : null);
        var account = definition.AccountField is { } accountField && Guid.TryParse(values.Get(accountField), out var id) ? id : (Guid?)null;
        return new Payment(amount, string.IsNullOrEmpty(frequency) ? null : frequency, account);
    }

    /// <summary>Monatlicher Betrag; null bei „einmalig“ und ohne Rhythmus.</summary>
    public static decimal? Monthly(DocObject item) =>
        Of(item) is { } payment && MyDocs.Shared.Payments.PaymentMath.TimesPerYear(payment.Frequency) is { } times ? payment.Amount * times / 12 : null;
}
