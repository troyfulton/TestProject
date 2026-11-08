namespace TestProject.Configuration;

/// <summary>
/// Directory listing pagination and sorting defaults.
/// </summary>
public sealed class BrowsingOptions
{
    public const string SectionName = "Browsing";

    public int DefaultPageSize { get; set; } = 100;

    public int MaxPageSize { get; set; } = 500;

    /// <summary>
    /// Default sort field: Name, Size, or LastModified.
    /// </summary>
    public string DefaultSort { get; set; } = "Name";

    /// <summary>
    /// When a directory has more files than this, directory total size is skipped
    /// and directorySizeComplete is reported as false.
    /// </summary>
    public int MaxFilesForSizeTotal { get; set; } = 2000;
}
