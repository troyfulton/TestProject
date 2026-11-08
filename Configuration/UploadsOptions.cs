namespace TestProject.Configuration;

/// <summary>
/// Resumable upload session and chunk transfer settings.
/// </summary>
public sealed class UploadsOptions
{
    public const string SectionName = "Uploads";

    /// <summary>
    /// Hard ceiling for chunk size so IIS/Kestrel request limits stay consistent with web.config.
    /// </summary>
    public const int MaxAllowedChunkSizeMB = 32;

    public bool Enabled { get; set; } = true;

    public int MaxFileSizeGB { get; set; } = 100;

    public int ChunkSizeMB { get; set; } = 8;

    public int StreamBufferKB { get; set; } = 64;

    public int MaxConcurrentUploads { get; set; } = 4;

    public int MaxRetries { get; set; } = 5;

    public int RetryInitialDelaySeconds { get; set; } = 2;

    public bool ResumeEnabled { get; set; } = true;

    public int SessionExpirationHours { get; set; } = 72;

    public bool VerifyChunkChecksum { get; set; } = true;

    public bool VerifyFinalChecksum { get; set; }

    public bool AllowOverwrite { get; set; }

    public long MaxFileSizeBytes => (long)MaxFileSizeGB * 1024L * 1024L * 1024L;

    public long ChunkSizeBytes => (long)ChunkSizeMB * 1024L * 1024L;

    public int StreamBufferBytes => StreamBufferKB * 1024;
}
