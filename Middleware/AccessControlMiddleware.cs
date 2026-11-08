using Microsoft.Extensions.Options;
using TestProject.Configuration;

namespace TestProject.Middleware;

/// <summary>
/// Fails closed for /api routes in non-anonymous deployments unless the request
/// is authenticated or carries the Azure App Service Easy Auth principal header.
/// </summary>
public sealed class AccessControlMiddleware
{
    /// <summary>
    /// Header injected by Azure App Service Authentication. The platform overwrites
    /// this header, so clients cannot forge a trusted principal.
    /// </summary>
    public const string EasyAuthPrincipalHeader = "X-MS-CLIENT-PRINCIPAL";

    private readonly RequestDelegate _next;
    private readonly SecurityOptions _security;

    public AccessControlMiddleware(RequestDelegate next, IOptions<SecurityOptions> security)
    {
        _next = next;
        _security = security.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_security.AllowAnonymousAccess || !IsApiRequest(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        var hasEasyAuthPrincipal = !string.IsNullOrWhiteSpace(context.Request.Headers[EasyAuthPrincipalHeader]);

        if (isAuthenticated || hasEasyAuthPrincipal)
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(
            """{"type":"https://httpstatuses.com/401","title":"Unauthorized","status":401,"detail":"Authentication is required. Configure App Service Authentication or set Security:AllowAnonymousAccess only for trusted networks.","errorCode":"auth_required"}""");
    }

    private static bool IsApiRequest(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
}
