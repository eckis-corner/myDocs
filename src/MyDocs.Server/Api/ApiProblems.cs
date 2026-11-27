namespace MyDocs.Server.Api;

/// <summary>Fehler der API als Problem-Details mit einem Code im Titel, den der Client übersetzt.</summary>
internal static class ApiProblems
{
    public static IResult Of(string code, int status = StatusCodes.Status400BadRequest) =>
        TypedResults.Problem(title: code, statusCode: status);
}
