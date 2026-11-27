using MyDocs.Shared.Household;

namespace MyDocs.ApiClient;

/// <summary>Personen des Haushalts.</summary>
public sealed class PersonClient(HttpClient http)
{
    public Task<List<PersonDto>> GetAllAsync(CancellationToken ct = default) => http.GetAsync<List<PersonDto>>("api/persons", ct);

    public Task<PersonDto> CreateAsync(PersonRequest request, CancellationToken ct = default) =>
        http.SendAsync<PersonRequest, PersonDto>(HttpMethod.Post, "api/persons", request, ct);

    public Task<PersonDto> UpdateAsync(Guid id, PersonRequest request, CancellationToken ct = default) =>
        http.SendAsync<PersonRequest, PersonDto>(HttpMethod.Put, $"api/persons/{id}", request, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, $"api/persons/{id}", ct);
}
