namespace MyDocs.Server.Data;

/// <summary>Person des Haushalts (77): Haushaltsmitglied mit Login oder Kind bzw. Beteiligter ohne Login.</summary>
public sealed class Person
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public DateOnly? BirthDate { get; set; }

    public string? Note { get; set; }

    /// <summary>Konto der Person, falls sie sich anmelden kann; höchstens eine Person je Konto.</summary>
    public Guid? UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsDeleted { get; set; }
}
