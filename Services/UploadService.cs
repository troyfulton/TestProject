using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TestProject.Configuration;
using TestProject.Exceptions;
using TestProject.Models;
using TestProject.Security;

namespace TestProject.Services;

/// <summary>
/// Tus-inspired resumable upload engine with persistent sessions and authoritative offsets.
/// Not fully tus-compliant. Designed for single-instance deployments with filesystem lock files.
/// </summary>
public sealed class UploadService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PathGuard _pathGuard;
    private readonly StorageRoot _storageRoot;
    private readonly UploadsOptions _options;
    private readonly FileStorageOptions _fileStorage;
    private readonly FeaturesOptions _features;
    private readonly SemaphoreSlim _createGate;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        PathGuard pathGuard,
        StorageRoot storageRoot,
        IOptions<UploadsOptions> options,
        IOptions<FileStorageOptions> fileStorage,
        IOptions<FeaturesOptions> features,
        ILogger<UploadService> logger)
    {
        _pathGuard = pathGuard;
        _storageRoot = storageRoot;
        _options = options.Value;
        _fileStorage = fileStorage.Value;
        _features = features.Value;
        _logger = logger;
        _createGate = new SemaphoreSlim(_options.MaxConcurrentUploads, _options.MaxConcurrentUploads);
    }

    public async Task<UploadSessionDto> CreateSessionAsync(CreateUploadRequest request, CancellationToken cancellationToken)
    {
        EnsureUploadEnabled();

        if (request.FileSize < 0 || request.FileSize > _options.MaxFileSizeBytes)
        {
            throw new FileManagerException(
                StatusCodes.Status413PayloadTooLarge,
                "Payload Too Large",
                $"File size must be between 0 and {_options.MaxFileSizeGB} GB.",
                "file_too_large");
        }

        _pathGuard.ValidateFileName(request.FileName);
        var parent = _pathGuard.Resolve(request.DestinationDirectory, mustExist: true, allowRoot: true, allowStaging: false);
        if (!Directory.Exists(parent.AbsolutePath))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Destination directory was not found.",
                "not_a_directory");
        }

        var destinationVirtual = _pathGuard.CombineRelative(parent.VirtualPath, request.FileName!);
        var destination = _pathGuard.Resolve(destinationVirtual, mustExist: false, allowRoot: false, allowStaging: false);

        if (!_options.AllowOverwrite && (File.Exists(destination.AbsolutePath) || Directory.Exists(destination.AbsolutePath)))
        {
            throw new FileManagerException(
                StatusCodes.Status409Conflict,
                "Conflict",
                "A file or folder with that name already exists.",
                "already_exists");
        }

        if (!await _createGate.WaitAsync(0, cancellationToken))
        {
            throw new FileManagerException(
                StatusCodes.Status429TooManyRequests,
                "Too Many Requests",
                "Too many concurrent uploads.",
                "upload_busy");
        }

        try
        {
            var uploadId = Guid.NewGuid().ToString("N");
            var sessionDir = GetSessionDirectory(uploadId);
            Directory.CreateDirectory(sessionDir);

            var now = DateTimeOffset.UtcNow;
            var metadata = new UploadSessionMetadata
            {
                UploadId = uploadId,
                DestinationDirectory = parent.VirtualPath,
                FileName = request.FileName!,
                DestinationPath = destination.VirtualPath,
                FileSize = request.FileSize,
                Offset = 0,
                Status = UploadSessionStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = now.AddHours(_options.SessionExpirationHours)
            };

            await WriteMetadataAtomicAsync(sessionDir, metadata, cancellationToken);

            // Create empty data file for zero-byte and subsequent appends.
            await using (var fs = new FileStream(GetDataPath(sessionDir), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await fs.FlushAsync(cancellationToken);
            }

            _logger.LogInformation("Created upload session {UploadId} for {Path}", uploadId, destination.VirtualPath);
            return ToDto(metadata);
        }
        finally
        {
            _createGate.Release();
        }
    }

    public async Task<UploadSessionDto> GetSessionAsync(string uploadId, CancellationToken cancellationToken)
    {
        EnsureUploadEnabled();
        await using var sessionLock = await AcquireLockAsync(uploadId, cancellationToken);
        var metadata = await LoadAndReconcileAsync(uploadId, cancellationToken);
        return ToDto(metadata);
    }

    public async Task<UploadSessionDto> WriteChunkAsync(
        string uploadId,
        long expectedOffset,
        string? checksumHex,
        Stream body,
        CancellationToken cancellationToken)
    {
        EnsureUploadEnabled();
        await using var sessionLock = await AcquireLockAsync(uploadId, cancellationToken);
        var metadata = await LoadAndReconcileAsync(uploadId, cancellationToken);

        if (metadata.Status != UploadSessionStatus.Active)
        {
            throw new FileManagerException(
                StatusCodes.Status409Conflict,
                "Conflict",
                "Upload session is not active.",
                "session_inactive");
        }

        if (DateTimeOffset.UtcNow > metadata.ExpiresAt)
        {
            throw new FileManagerException(
                StatusCodes.Status409Conflict,
                "Conflict",
                "Upload session has expired.",
                "session_expired");
        }

        if (expectedOffset != metadata.Offset)
        {
            throw new FileManagerException(
                StatusCodes.Status409Conflict,
                "Conflict",
                $"Expected offset {metadata.Offset} but received {expectedOffset}.",
                "offset_mismatch");
        }

        if (expectedOffset > metadata.FileSize)
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Offset exceeds declared file size.",
                "invalid_offset");
        }

        var remaining = metadata.FileSize - metadata.Offset;
        var maxChunk = Math.Min(_options.ChunkSizeBytes, remaining);
        if (maxChunk <= 0 && metadata.FileSize > 0)
        {
            throw new FileManagerException(
                StatusCodes.Status409Conflict,
                "Conflict",
                "Upload already has all bytes. Call complete.",
                "already_complete_bytes");
        }

        var sessionDir = GetSessionDirectory(uploadId);
        var dataPath = GetDataPath(sessionDir);
        var buffer = new byte[_options.StreamBufferBytes];
        long written = 0;

        IncrementalHash? hasher = null;
        if (_options.VerifyChunkChecksum)
        {
            hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }

        try
        {
            await using var file = new FileStream(
                dataPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None,
                _options.StreamBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            file.Seek(metadata.Offset, SeekOrigin.Begin);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (written >= maxChunk)
                {
                    // Reject surplus bytes beyond the allowed chunk.
                    var probe = await body.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
                    if (probe > 0)
                    {
                        throw new FileManagerException(
                            StatusCodes.Status413PayloadTooLarge,
                            "Payload Too Large",
                            "Chunk exceeds the allowed size for the remaining bytes.",
                            "chunk_too_large");
                    }

                    break;
                }

                var toRead = (int)Math.Min(buffer.Length, maxChunk - written);
                var read = await body.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hasher?.AppendData(buffer, 0, read);
                written += read;
            }

            await file.FlushAsync(cancellationToken);
            file.Flush(true);
        }
        catch (FileManagerException)
        {
            // Truncate back to confirmed offset so a failed chunk never advances state.
            TruncateDataFile(dataPath, metadata.Offset);
            throw;
        }
        catch
        {
            TruncateDataFile(dataPath, metadata.Offset);
            throw;
        }

        if (written == 0 && metadata.FileSize > metadata.Offset)
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Chunk body was empty.",
                "empty_chunk");
        }

        if (_options.VerifyChunkChecksum)
        {
            if (string.IsNullOrWhiteSpace(checksumHex))
            {
                TruncateDataFile(dataPath, metadata.Offset);
                throw new FileManagerException(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Chunk checksum is required.",
                    "checksum_required");
            }

            var actual = Convert.ToHexString(hasher!.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, checksumHex.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                TruncateDataFile(dataPath, metadata.Offset);
                throw new FileManagerException(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Chunk checksum mismatch.",
                    "checksum_mismatch");
            }
        }

        // Advance offset only after durable data and successful validation.
        metadata.Offset += written;
        metadata.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteMetadataAtomicAsync(sessionDir, metadata, cancellationToken);

        return ToDto(metadata);
    }

    public async Task<UploadCompletedDto> CompleteAsync(string uploadId, string? finalSha256, CancellationToken cancellationToken)
    {
        EnsureUploadEnabled();
        await using var sessionLock = await AcquireLockAsync(uploadId, cancellationToken);
        var metadata = await LoadAndReconcileAsync(uploadId, cancellationToken);
        var sessionDir = GetSessionDirectory(uploadId);

        if (metadata.Status == UploadSessionStatus.Complete)
        {
            return new UploadCompletedDto
            {
                UploadId = metadata.UploadId,
                Path = metadata.DestinationPath,
                FileName = metadata.FileName,
                FileSize = metadata.FileSize
            };
        }

        if (metadata.Status != UploadSessionStatus.Active)
        {
            throw new FileManagerException(
                StatusCodes.Status409Conflict,
                "Conflict",
                "Upload session is not active.",
                "session_inactive");
        }

        if (metadata.Offset != metadata.FileSize)
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                $"Upload incomplete. Confirmed {metadata.Offset} of {metadata.FileSize} bytes.",
                "incomplete");
        }

        var dataPath = GetDataPath(sessionDir);
        var length = new FileInfo(dataPath).Length;
        if (length != metadata.FileSize)
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Stored length does not match the declared file size.",
                "length_mismatch");
        }

        if (_options.VerifyFinalChecksum)
        {
            if (string.IsNullOrWhiteSpace(finalSha256))
            {
                throw new FileManagerException(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Final checksum is required.",
                    "checksum_required");
            }

            var hash = await ComputeFileSha256Async(dataPath, cancellationToken);
            if (!string.Equals(hash, finalSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new FileManagerException(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Final checksum mismatch.",
                    "checksum_mismatch");
            }

            metadata.FinalSha256 = hash;
        }

        var destination = _pathGuard.Resolve(metadata.DestinationPath, mustExist: false, allowRoot: false, allowStaging: false);
        if (!_options.AllowOverwrite && (File.Exists(destination.AbsolutePath) || Directory.Exists(destination.AbsolutePath)))
        {
            throw new FileManagerException(
                StatusCodes.Status409Conflict,
                "Conflict",
                "A file or folder with that name already exists.",
                "already_exists");
        }

        var destinationDirectory = Path.GetDirectoryName(destination.AbsolutePath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(destination.AbsolutePath) && _options.AllowOverwrite)
        {
            File.Delete(destination.AbsolutePath);
        }

        // Same-volume atomic publish.
        File.Move(dataPath, destination.AbsolutePath);

        metadata.Status = UploadSessionStatus.Complete;
        metadata.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteMetadataAtomicAsync(sessionDir, metadata, cancellationToken);

        // Best-effort cleanup of staging leftovers after successful publish.
        TryDeleteFile(GetLockPath(sessionDir));
        // Keep meta briefly for duplicate complete; cleanup service removes expired/complete sessions.

        _logger.LogInformation("Completed upload {UploadId} to {Path}", uploadId, metadata.DestinationPath);

        return new UploadCompletedDto
        {
            UploadId = metadata.UploadId,
            Path = metadata.DestinationPath,
            FileName = metadata.FileName,
            FileSize = metadata.FileSize
        };
    }

    public async Task CancelAsync(string uploadId, CancellationToken cancellationToken)
    {
        EnsureUploadEnabled();
        var sessionDir = GetSessionDirectory(uploadId);
        if (!Directory.Exists(sessionDir))
        {
            return;
        }

        var sessionLock = await AcquireLockAsync(uploadId, cancellationToken);
        try
        {
            var metadata = await LoadMetadataAsync(sessionDir, cancellationToken);
            metadata.Status = UploadSessionStatus.Cancelled;
            metadata.UpdatedAt = DateTimeOffset.UtcNow;
            await WriteMetadataAtomicAsync(sessionDir, metadata, cancellationToken);
        }
        finally
        {
            // Release the lock file before deleting the session directory.
            await sessionLock.DisposeAsync();
        }

        TryDeleteDirectory(sessionDir);
        _logger.LogInformation("Cancelled upload {UploadId}", uploadId);
    }

    public async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        var staging = _storageRoot.StagingPath;
        if (!Directory.Exists(staging))
        {
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(staging))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var metaPath = Path.Combine(dir, "meta.json");
                if (!File.Exists(metaPath))
                {
                    continue;
                }

                await using var sessionLock = await TryAcquireLockForPathAsync(dir, cancellationToken);
                if (sessionLock is null)
                {
                    continue;
                }

                var metadata = await LoadMetadataAsync(dir, cancellationToken);
                var expired = DateTimeOffset.UtcNow > metadata.ExpiresAt;
                var completeAndOld = metadata.Status == UploadSessionStatus.Complete
                                     && DateTimeOffset.UtcNow - metadata.UpdatedAt > TimeSpan.FromHours(1);

                if (expired || completeAndOld || metadata.Status == UploadSessionStatus.Cancelled)
                {
                    await sessionLock.DisposeAsync();
                    TryDeleteDirectory(dir);
                    _logger.LogInformation("Cleaned upload session {UploadId}", metadata.UploadId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean upload session directory.");
            }
        }
    }

    private void EnsureUploadEnabled()
    {
        if (!_features.EnableUpload || !_options.Enabled)
        {
            throw new FileManagerException(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "Uploads are disabled.",
                "feature_disabled");
        }
    }

    private async Task<UploadSessionMetadata> LoadAndReconcileAsync(string uploadId, CancellationToken cancellationToken)
    {
        var sessionDir = GetSessionDirectory(uploadId);
        if (!Directory.Exists(sessionDir))
        {
            throw new FileManagerException(
                StatusCodes.Status404NotFound,
                "Not Found",
                "Upload session was not found.",
                "not_found");
        }

        var metadata = await LoadMetadataAsync(sessionDir, cancellationToken);
        if (metadata.Status != UploadSessionStatus.Active)
        {
            return metadata;
        }

        var dataPath = GetDataPath(sessionDir);
        long fileLength = File.Exists(dataPath) ? new FileInfo(dataPath).Length : 0;
        var reconciled = Math.Min(metadata.Offset, fileLength);

        if (reconciled != metadata.Offset || fileLength != reconciled)
        {
            TruncateDataFile(dataPath, reconciled);
            metadata.Offset = reconciled;
            metadata.UpdatedAt = DateTimeOffset.UtcNow;
            await WriteMetadataAtomicAsync(sessionDir, metadata, cancellationToken);
            _logger.LogWarning("Reconciled upload {UploadId} offset to {Offset}", uploadId, reconciled);
        }

        return metadata;
    }

    private async Task<UploadSessionMetadata> LoadMetadataAsync(string sessionDir, CancellationToken cancellationToken)
    {
        var path = GetMetaPath(sessionDir);
        if (!File.Exists(path))
        {
            throw new FileManagerException(
                StatusCodes.Status404NotFound,
                "Not Found",
                "Upload session metadata was not found.",
                "not_found");
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var metadata = await JsonSerializer.DeserializeAsync<UploadSessionMetadata>(stream, JsonOptions, cancellationToken);
        if (metadata is null)
        {
            throw new FileManagerException(
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "Upload metadata could not be read.",
                "metadata_corrupt");
        }

        return metadata;
    }

    private static async Task WriteMetadataAtomicAsync(
        string sessionDir,
        UploadSessionMetadata metadata,
        CancellationToken cancellationToken)
    {
        var finalPath = GetMetaPath(sessionDir);
        var tempPath = finalPath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }

        File.Move(tempPath, finalPath, overwrite: true);
    }

    private async Task<SessionLock> AcquireLockAsync(string uploadId, CancellationToken cancellationToken)
    {
        var sessionDir = GetSessionDirectory(uploadId);
        Directory.CreateDirectory(sessionDir);
        var lockPath = GetLockPath(sessionDir);

        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new SessionLock(stream);
            }
            catch (IOException)
            {
                await Task.Delay(25, cancellationToken);
            }
        }

        throw new FileManagerException(
            StatusCodes.Status409Conflict,
            "Conflict",
            "Another writer is modifying this upload session.",
            "upload_locked");
    }

    private static async Task<SessionLock?> TryAcquireLockForPathAsync(string sessionDir, CancellationToken cancellationToken)
    {
        var lockPath = GetLockPath(sessionDir);
        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new SessionLock(stream);
        }
        catch (IOException)
        {
            await Task.Yield();
            return null;
        }
    }

    private string GetSessionDirectory(string uploadId)
    {
        if (string.IsNullOrWhiteSpace(uploadId)
            || uploadId.Length > 64
            || uploadId.Any(c => !char.IsAsciiHexDigit(c)))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Invalid upload id.",
                "invalid_upload_id");
        }

        return Path.Combine(_storageRoot.StagingPath, uploadId.ToLowerInvariant());
    }

    private static string GetDataPath(string sessionDir) => Path.Combine(sessionDir, "data.bin");

    private static string GetMetaPath(string sessionDir) => Path.Combine(sessionDir, "meta.json");

    private static string GetLockPath(string sessionDir) => Path.Combine(sessionDir, "session.lock");

    private static void TruncateDataFile(string dataPath, long length)
    {
        if (!File.Exists(dataPath))
        {
            return;
        }

        using var fs = new FileStream(dataPath, FileMode.Open, FileAccess.Write, FileShare.None);
        fs.SetLength(Math.Max(0, length));
        fs.Flush(true);
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Cleanup is best-effort; next cleanup pass retries.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private static UploadSessionDto ToDto(UploadSessionMetadata metadata) =>
        new()
        {
            UploadId = metadata.UploadId,
            DestinationPath = metadata.DestinationPath,
            FileName = metadata.FileName,
            FileSize = metadata.FileSize,
            Offset = metadata.Offset,
            Status = metadata.Status,
            ExpiresAt = metadata.ExpiresAt
        };

    private sealed class SessionLock : IAsyncDisposable
    {
        private FileStream? _stream;

        public SessionLock(FileStream stream)
        {
            _stream = stream;
        }

        public ValueTask DisposeAsync()
        {
            if (_stream is not null)
            {
                _stream.Dispose();
                _stream = null;
            }

            return ValueTask.CompletedTask;
        }
    }
}
