using MyDocs.Shared;

namespace MyDocs.Server.Infrastructure.Auth;

/// <summary>Schreibende Aufrufe der API brauchen den Header <c>X-MyDocs-Request</c>; ein fremdes Formular kann ihn nicht setzen.</summary>
internal sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
            && request.Headers[ApiHeaders.Csrf] != ApiHeaders.CsrfValue)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return context.Response.WriteAsJsonAsync(new { title = "csrf-header-missing", status = 400 });
        }

        return next(context);
    }
}
