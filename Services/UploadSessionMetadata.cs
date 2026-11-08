namespace TestProject.Services;

/// <summary>
/// Durable metadata for a resumable upload session persisted as meta.json.
/// </summary>
public sealed class UploadSessionMetadata
{
    public required string UploadId { get; set; }

    public required string DestinationDirectory { get; set; }

    public required string FileName { get; set; }

    public required string DestinationPath { get; set; }

    public required long FileSize { get; set; }

    public required long Offset { get; set; }

    public required string Status { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }

    public string? FinalSha256 { get; set; }
}

public static class UploadSessionStatus
{
    public const string Active = "Active";
    public const string Complete = "Complete";
    public const string Cancelled = "Cancelled";
}
