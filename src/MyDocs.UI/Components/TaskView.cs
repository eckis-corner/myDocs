using Microsoft.Extensions.Localization;
using MudBlazor;
using MyDocs.Shared.Tasks;
using MyDocs.UI.Resources;

namespace MyDocs.UI.Components;

/// <summary>Anzeige von Vorgängen: übersetzter Titel automatischer Vorgänge, Farbe des Status, überfällig.</summary>
public static class TaskView
{
    public static string Title(TaskDto task, IStringLocalizer<SharedResource> l)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(l);
        return task.TitleKey is { } key && l[$"TaskTitle_{key}"] is { ResourceNotFound: false } text ? text.Value : task.Title;
    }

    public static bool IsOverdue(TaskDto task, DateOnly today) =>
        !TaskStates.Closed.Contains(task.State) && task.State != TaskStates.Waiting && task.DueOn is { } due && due < today;

    public static Color StateColor(TaskDto task) => task.State switch
    {
        TaskStates.Done => Color.Success,
        TaskStates.Discarded => Color.Default,
        TaskStates.Waiting => Color.Info,
        _ when IsOverdue(task, DateOnly.FromDateTime(DateTime.Today)) => Color.Error,
        TaskStates.InProgress => Color.Primary,
        _ => Color.Warning,
    };
}
