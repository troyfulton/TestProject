namespace TestProject.Configuration;

/// <summary>
/// Filesystem storage root and overwrite/visibility policy.
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Absolute or content-root-relative path for user file storage. Must not be under wwwroot.
    /// </summary>
    public string RootPath { get; set; } = "FileData";

    /// <summary>
    /// Logical maximum file size in gigabytes for stored files.
    /// </summary>
    public int MaxFileSizeGB { get; set; } = 100;

    /// <summary>
    /// When false, move/copy/upload completion rejects existing destinations.
    /// </summary>
    public bool AllowOverwrite { get; set; }

    /// <summary>
    /// When false, hidden and system entries are omitted from listings.
    /// </summary>
    public bool ShowHiddenFiles { get; set; }

    /// <summary>
    /// Buffer size in kilobytes used when streaming copy operations.
    /// </summary>
    public int StreamBufferKB { get; set; } = 64;

    /// <summary>
    /// When true, creates home/readme.md if the storage root has no user folders at startup.
    /// </summary>
    public bool SeedDefaultHomeWhenEmpty { get; set; } = true;

    public long MaxFileSizeBytes => (long)MaxFileSizeGB * 1024L * 1024L * 1024L;
}
