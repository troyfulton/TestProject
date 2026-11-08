namespace TestProject.Models;

/// <summary>
/// A single file or folder entry returned by browse and search APIs.
/// </summary>
public sealed class DirectoryEntryDto
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public required bool IsDirectory { get; init; }

    public string? Extension { get; init; }

    public long? Size { get; init; }

    public DateTimeOffset? LastModified { get; init; }
}
