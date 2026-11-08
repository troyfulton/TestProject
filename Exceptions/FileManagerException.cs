namespace TestProject.Exceptions;

/// <summary>
/// Application exception mapped to a safe HTTP ProblemDetails response.
/// </summary>
public sealed class FileManagerException : Exception
{
    public FileManagerException(int statusCode, string title, string detail, string? errorCode = null)
        : base(detail)
    {
        StatusCode = statusCode;
        Title = title;
        Detail = detail;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }

    public string Title { get; }

    public string Detail { get; }

    public string? ErrorCode { get; }
}
