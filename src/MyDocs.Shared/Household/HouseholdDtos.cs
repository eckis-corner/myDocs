namespace MyDocs.Shared.Household;

/// <summary>Rolle im Haushalt (32): Mitglieder lesen und schreiben, Gäste lesen nur. Admin ist ein Zusatzrecht und immer Mitglied.</summary>
public static class HouseholdRoles
{
    public const string Member = "member";
    public const string Guest = "guest";
    public const string Admin = "admin";

    public static IReadOnlyList<string> All { get; } = [Member, Guest];
}

/// <summary>Person des Haushalts (77), mit oder ohne Login.</summary>
public sealed record PersonDto(Guid Id, string DisplayName, DateOnly? BirthDate, Guid? UserId, string? Note);

public sealed record PersonRequest(string DisplayName, DateOnly? BirthDate, string? Note);

/// <summary>Fehlercodes rund um Personen und Benutzer; Texte unter <c>HouseholdError_{Code}</c>.</summary>
public static class HouseholdErrors
{
    public const string NameRequired = "name-required";
    public const string PersonHasUser = "person-has-user";
    public const string NotFound = "not-found";
    public const string EmailTaken = "email-taken";
    public const string EmailInvalid = "email-invalid";
    public const string RoleInvalid = "role-invalid";
    public const string LastAdmin = "last-admin";
    public const string NotSelf = "not-self";
}
