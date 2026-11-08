namespace TestProject.Models;

/// <summary>
/// Filename/folder-name search response with truncation metadata.
/// </summary>
public sealed class SearchResultDto
{
    public required string Query { get; init; }

    public required string Path { get; init; }

    public required bool Recursive { get; init; }

    public required IReadOnlyList<DirectoryEntryDto> Items { get; init; }

    public required int ResultCount { get; init; }

    public required int MaxResults { get; init; }

    /// <summary>True when the result set was capped or timed out before a full scan.</summary>
    public required bool Truncated { get; init; }

    public string? TruncationReason { get; init; }
}
