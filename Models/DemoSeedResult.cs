namespace TestProject.Models;

/// <summary>
/// Counts from a demo-data seed. Paths are intentionally omitted.
/// </summary>
public sealed class DemoSeedResult
{
    /// <summary>Directories created during this run.</summary>
    public required int CreatedDirectories { get; init; }

    /// <summary>Files created during this run.</summary>
    public required int CreatedFiles { get; init; }

    /// <summary>Directories and files that were already present and left unchanged.</summary>
    public required int SkippedExisting { get; init; }

    /// <summary>Wall-clock time spent seeding, in milliseconds.</summary>
    public required long ElapsedMilliseconds { get; init; }
}
