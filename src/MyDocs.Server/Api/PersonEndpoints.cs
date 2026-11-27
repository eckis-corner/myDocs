using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Shared.Household;

namespace MyDocs.Server.Api;

/// <summary>Personen des Haushalts (77). Alle Angemeldeten lesen, Mitglieder legen an und ändern.</summary>
internal static class PersonEndpoints
{
    public const string MemberPolicy = HouseholdRoles.Member;

    public static IEndpointRouteBuilder MapPersonEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/persons").RequireAuthorization();
        group.MapGet("/", async (MyDocsDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.Persons.OrderBy(p => p.DisplayName).Select(p => ToDto(p)).ToListAsync(ct)));
        group.MapPost("/", CreateAsync).RequireAuthorization(MemberPolicy);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(MemberPolicy);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(MemberPolicy);
        return app;
    }

    private static async Task<IResult> CreateAsync(PersonRequest request, MyDocsDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }

        var person = new Person
        {
            Id = Guid.CreateVersion7(),
            DisplayName = request.DisplayName.Trim(),
            BirthDate = request.BirthDate,
            Note = Clean(request.Note),
            CreatedAt = time.GetUtcNow(),
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(person));
    }

    private static async Task<IResult> UpdateAsync(Guid id, PersonRequest request, MyDocsDbContext db, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }

        if (await db.Persons.FirstOrDefaultAsync(p => p.Id == id, ct) is not { } person)
        {
            return ApiProblems.Of(HouseholdErrors.NotFound, StatusCodes.Status404NotFound);
        }

        // Der Name einer Person mit Konto ist der Name des Kontos; den ändert der Administrator.
        if (person.UserId is null)
        {
            person.DisplayName = request.DisplayName.Trim();
        }

        person.BirthDate = request.BirthDate;
        person.Note = Clean(request.Note);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(person));
    }

    private static async Task<IResult> DeleteAsync(Guid id, MyDocsDbContext db, CancellationToken ct)
    {
        if (await db.Persons.FirstOrDefaultAsync(p => p.Id == id, ct) is not { } person)
        {
            return ApiProblems.Of(HouseholdErrors.NotFound, StatusCodes.Status404NotFound);
        }

        if (person.UserId is not null)
        {
            return ApiProblems.Of(HouseholdErrors.PersonHasUser);
        }

        person.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static IResult? Validate(PersonRequest request) =>
        string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 200 ? ApiProblems.Of(HouseholdErrors.NameRequired) : null;

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 2000)];

    private static PersonDto ToDto(Person p) => new(p.Id, p.DisplayName, p.BirthDate, p.UserId, p.Note);
}
