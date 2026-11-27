using MyDocs.Shared.Audit;

namespace MyDocs.UI.Services;

/// <summary>
/// Admin-Einsicht (31): Der Administrator schaltet sie bewusst ein; dann schickt der Client den Header
/// <see cref="AdminViewHeader"/> mit und der Server zeigt auch fremde private Einträge, nur zum Lesen. Sie gilt nur bis
/// zum Neuladen oder Abmelden.
/// </summary>
public sealed class AdminViewService(HttpClient http)
{
    public bool IsOn { get; private set; }

    public event Action? Changed;

    public void Set(bool on)
    {
        if (on == IsOn)
        {
            return;
        }

        IsOn = on;
        http.DefaultRequestHeaders.Remove(AdminViewHeader.Name);
        if (on)
        {
            http.DefaultRequestHeaders.Add(AdminViewHeader.Name, AdminViewHeader.Value);
        }

        Changed?.Invoke();
    }
}
