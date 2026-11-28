using MyDocs.Shared.Payments;

namespace MyDocs.ApiClient;

/// <summary>Übersicht „Zahlungen“ (M6.1).</summary>
public sealed class PaymentClient(HttpClient http)
{
    public Task<List<PaymentDto>> ListAsync(bool household, CancellationToken ct = default) =>
        http.GetAsync<List<PaymentDto>>($"api/payments?scope={(household ? "household" : "mine")}", ct);
}
