using MyDocs.ApiClient;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;

namespace MyDocs.UI.Services;

/// <summary>
/// Was Formulare und Datenblätter nachschlagen: alle sichtbaren Objekte (für Verweise und Ablageorte) und die Personen.
/// Wird je Seite einmal geladen; nach dem Speichern lädt die Seite neu.
/// </summary>
public sealed class ObjectDirectory(ObjectClient objects, PersonClient persons)
{
    private Dictionary<Guid, ObjectSummaryDto> _objects = [];
    private Dictionary<Guid, PersonDto> _persons = [];

    public IReadOnlyCollection<ObjectSummaryDto> Objects => _objects.Values;

    public IReadOnlyCollection<PersonDto> Persons => _persons.Values;

    /// <summary>Personen mit Konto: für Zuständigkeit und Freigaben.</summary>
    public IEnumerable<PersonDto> Accounts => _persons.Values.Where(p => p.UserId is not null).OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase);

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var objectsTask = objects.ListAsync(ct: ct);
        var personsTask = persons.GetAllAsync(ct);
        _objects = (await objectsTask).ToDictionary(o => o.Id);
        _persons = (await personsTask).ToDictionary(p => p.Id);
    }

    public string? TitleOf(Guid id) => _objects.TryGetValue(id, out var item) ? item.Title : null;

    public ObjectSummaryDto? Find(Guid id) => _objects.GetValueOrDefault(id);

    public string? PersonName(Guid? id) => id is { } p && _persons.TryGetValue(p, out var person) ? person.DisplayName : null;

    public IEnumerable<ObjectSummaryDto> OfTypes(IReadOnlyList<string> types, Guid? except = null) =>
        _objects.Values.Where(o => o.Id != except && (types.Count == 0 || types.Contains(o.TypeKey)))
            .OrderBy(o => o.Title, StringComparer.CurrentCultureIgnoreCase);

    public IEnumerable<ObjectSummaryDto> Locations => OfTypes([MyCommon.Catalog.ObjectCatalog.LocationType]);

    /// <summary>Objekte, die per Verweisfeld oder Ablageort auf <paramref name="id"/> zeigen (Verknüpfung in beide Richtungen).</summary>
    public IEnumerable<ObjectSummaryDto> ReferencesTo(Guid id)
    {
        var text = id.ToString();
        return _objects.Values.Where(o => o.Id != id && o.Fields.ContainsValue(text))
            .OrderBy(o => o.Title, StringComparer.CurrentCultureIgnoreCase);
    }
}
