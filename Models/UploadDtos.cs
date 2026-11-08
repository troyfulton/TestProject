namespace TestProject.Models;

public sealed class CreateUploadRequest
{
    public string? DestinationDirectory { get; set; }

    public string? FileName { get; set; }

    public long FileSize { get; set; }
}

public sealed class UploadSessionDto
{
    public required string UploadId { get; init; }

    public required string DestinationPath { get; init; }

    public required string FileName { get; init; }

    public required long FileSize { get; init; }

    public required long Offset { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed class CompleteUploadRequest
{
    /// <summary>
    /// Optional final SHA-256 hex digest when server final checksum verification is enabled.
    /// </summary>
    public string? Sha256 { get; set; }
}

public sealed class UploadCompletedDto
{
    public required string UploadId { get; init; }

    public required string Path { get; init; }

    public required string FileName { get; init; }

    public required long FileSize { get; init; }
}
