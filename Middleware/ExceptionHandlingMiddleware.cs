using System.Text.Json;
using TestProject.Exceptions;

namespace TestProject.Middleware;

/// <summary>
/// Converts known application failures into ProblemDetails without leaking server paths.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected; nginx-style 499 without a body.
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (FileManagerException ex)
        {
            _logger.LogWarning(ex, "Handled file manager error {ErrorCode}", ex.ErrorCode);
            await WriteProblemAsync(context, ex.StatusCode, ex.Title, ex.Detail, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            var detail = _environment.IsDevelopment()
                ? "An unexpected error occurred. See server logs for details."
                : "An unexpected error occurred.";
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "Internal Server Error", detail, "internal_error");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string? errorCode)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var payload = new Dictionary<string, object?>
        {
            ["type"] = $"https://httpstatuses.com/{statusCode}",
            ["title"] = title,
            ["status"] = statusCode,
            ["detail"] = detail
        };

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            payload["errorCode"] = errorCode;
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
