using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;

namespace MyDocs.ApiClient;

/// <summary>Vorgänge, Kommentare und der Lebenszyklus der Objekte.</summary>
public sealed class TaskClient(HttpClient http)
{
    public Task<List<TaskDto>> ListAsync(bool mine = false, bool open = true, CancellationToken ct = default) =>
        http.GetAsync<List<TaskDto>>($"api/tasks?scope={(mine ? "mine" : "all")}&open={(open ? "true" : "false")}", ct);

    public Task<List<TaskDto>> ForObjectAsync(Guid objectId, CancellationToken ct = default) => http.GetAsync<List<TaskDto>>($"api/objects/{objectId}/tasks", ct);

    public Task<TaskDto> GetAsync(Guid id, CancellationToken ct = default) => http.GetAsync<TaskDto>($"api/tasks/{id}", ct);

    public Task<TaskDto> CreateAsync(TaskSaveRequest request, CancellationToken ct = default) =>
        http.SendAsync<TaskSaveRequest, TaskDto>(HttpMethod.Post, "api/tasks", request, ct);

    public Task<TaskDto> UpdateAsync(Guid id, TaskSaveRequest request, CancellationToken ct = default) =>
        http.SendAsync<TaskSaveRequest, TaskDto>(HttpMethod.Put, $"api/tasks/{id}", request, ct);

    public Task<TaskDto> SetStateAsync(Guid id, TaskStateRequest request, CancellationToken ct = default) =>
        http.SendAsync<TaskStateRequest, TaskDto>(HttpMethod.Post, $"api/tasks/{id}/state", request, ct);

    public Task<List<CommentDto>> TaskCommentsAsync(Guid id, CancellationToken ct = default) => http.GetAsync<List<CommentDto>>($"api/tasks/{id}/comments", ct);

    public Task<CommentDto> AddTaskCommentAsync(Guid id, string text, CancellationToken ct = default) =>
        http.SendAsync<CommentRequest, CommentDto>(HttpMethod.Post, $"api/tasks/{id}/comments", new CommentRequest(text), ct);

    public Task<List<CommentDto>> ObjectCommentsAsync(Guid id, CancellationToken ct = default) => http.GetAsync<List<CommentDto>>($"api/objects/{id}/comments", ct);

    public Task<CommentDto> AddObjectCommentAsync(Guid id, string text, CancellationToken ct = default) =>
        http.SendAsync<CommentRequest, CommentDto>(HttpMethod.Post, $"api/objects/{id}/comments", new CommentRequest(text), ct);

    public Task DeleteCommentAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, $"api/comments/{id}", ct);

    public Task<TaskDto> PostponeAsync(Guid id, TaskPostponeRequest request, CancellationToken ct = default) =>
        http.SendAsync<TaskPostponeRequest, TaskDto>(HttpMethod.Post, $"api/tasks/{id}/postpone", request, ct);

    public Task<ObjectDto> SetReviewAsync(Guid objectId, ReviewSettingsRequest request, CancellationToken ct = default) =>
        http.SendAsync<ReviewSettingsRequest, ObjectDto>(HttpMethod.Put, $"api/objects/{objectId}/review", request, ct);

    public Task<ReviewNowResult> ReviewNowAsync(Guid objectId, CancellationToken ct = default) => http.PostForAsync<ReviewNowResult>($"api/objects/{objectId}/review/now", ct);

    public Task<ObjectDto> ChangeLifecycleAsync(Guid objectId, LifecycleChangeRequest request, CancellationToken ct = default) =>
        http.SendAsync<LifecycleChangeRequest, ObjectDto>(HttpMethod.Post, $"api/objects/{objectId}/lifecycle", request, ct);
}

/// <summary>Antwort von „Jetzt prüfen“: der offene Vorgang „Prüfung“.</summary>
public sealed record ReviewNowResult(Guid TaskId);
