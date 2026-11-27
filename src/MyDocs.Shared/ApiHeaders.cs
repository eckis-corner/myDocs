namespace MyDocs.Shared;

/// <summary>Header, die der Client bei jedem Aufruf mitschickt.</summary>
public static class ApiHeaders
{
    /// <summary>Schutz gegen CSRF: Schreibende Aufrufe ohne diesen Header lehnt der Server ab.</summary>
    public const string Csrf = "X-MyDocs-Request";

    public const string CsrfValue = "1";
}
