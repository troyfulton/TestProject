namespace TestProject.Configuration;

/// <summary>
/// Production fail-closed access control for destructive filesystem APIs.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// When false, /api requests require authenticated identity or App Service Easy Auth principal.
    /// Development sets this true so local F5 works without auth.
    /// </summary>
    public bool AllowAnonymousAccess { get; set; }
}
