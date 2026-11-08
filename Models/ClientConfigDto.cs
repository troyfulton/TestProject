namespace TestProject.Models;

/// <summary>
/// Public, non-sensitive settings exposed to the SPA.
/// </summary>
public sealed class ClientConfigDto
{
    public required FeaturesClientConfig Features { get; init; }

    public required BrowsingClientConfig Browsing { get; init; }

    public required SearchClientConfig Search { get; init; }

    public required UploadsClientConfig Uploads { get; init; }
}

public sealed class FeaturesClientConfig
{
    public required bool EnableUpload { get; init; }
    public required bool EnableDownload { get; init; }
    public required bool EnableSearch { get; init; }
    public required bool EnableCreateFolder { get; init; }
    public required bool EnableDelete { get; init; }
    public required bool EnableMove { get; init; }
    public required bool EnableCopy { get; init; }
    public required bool EnableDialogMode { get; init; }

    /// <summary>True when the header control may generate the demonstration file tree.</summary>
    public required bool EnableGenerateTestData { get; init; }
}

public sealed class BrowsingClientConfig
{
    public required int DefaultPageSize { get; init; }
    public required int MaxPageSize { get; init; }
    public required string DefaultSort { get; init; }
}

public sealed class SearchClientConfig
{
    public required bool Enabled { get; init; }
    public required int MaxResults { get; init; }
}

public sealed class UploadsClientConfig
{
    public required bool Enabled { get; init; }
    public required int MaxFileSizeGB { get; init; }
    public required int ChunkSizeMB { get; init; }
    public required int MaxRetries { get; init; }
    public required int RetryInitialDelaySeconds { get; init; }
    public required bool ResumeEnabled { get; init; }
    public required bool VerifyChunkChecksum { get; init; }
}
