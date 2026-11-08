namespace TestProject.Models;

/// <summary>
/// Paginated directory listing with page-scoped and directory-scoped aggregates.
/// </summary>
public sealed class DirectoryListingDto
{
    public required string Path { get; init; }

    public string? ParentPath { get; init; }

    public required IReadOnlyList<DirectoryEntryDto> Items { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public required string Sort { get; init; }

    public required string Direction { get; init; }

    /// <summary>Number of items on the current page.</summary>
    public required int PageItemCount { get; init; }

    /// <summary>Total size of files on the current page only.</summary>
    public required long PageFileSizeBytes { get; init; }

    /// <summary>Total folders in the directory (all pages).</summary>
    public required int DirectoryFolderCount { get; init; }

    /// <summary>Total files in the directory (all pages).</summary>
    public required int DirectoryFileCount { get; init; }

    /// <summary>Total bytes of files in the directory when computed.</summary>
    public long? DirectoryFileSizeBytes { get; init; }

    /// <summary>False when directory size was skipped due to size bounds.</summary>
    public required bool DirectorySizeComplete { get; init; }

    public required int TotalItems { get; init; }

    public required int TotalPages { get; init; }
}
