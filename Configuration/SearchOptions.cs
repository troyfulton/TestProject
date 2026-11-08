namespace TestProject.Configuration;

/// <summary>
/// Filename and folder-name search limits.
/// </summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    public bool Enabled { get; set; } = true;

    public int MaxResults { get; set; } = 500;

    public int TimeoutSeconds { get; set; } = 15;

    public int MaxConcurrentSearches { get; set; } = 4;

    public bool IncludeSubdirectories { get; set; } = true;
}
