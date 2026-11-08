using Microsoft.Extensions.Options;
using TestProject.Configuration;
using TestProject.Exceptions;
using TestProject.Models;
using TestProject.Security;

namespace TestProject.Services;

/// <summary>
/// Bounded filename and folder-name search without content indexing.
/// </summary>
public sealed class SearchService
{
    private readonly PathGuard _pathGuard;
    private readonly SearchOptions _options;
    private readonly FileStorageOptions _storage;
    private readonly SemaphoreSlim _concurrency;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        PathGuard pathGuard,
        IOptions<SearchOptions> options,
        IOptions<FileStorageOptions> storage,
        ILogger<SearchService> logger)
    {
        _pathGuard = pathGuard;
        _options = options.Value;
        _storage = storage.Value;
        _logger = logger;
        _concurrency = new SemaphoreSlim(_options.MaxConcurrentSearches, _options.MaxConcurrentSearches);
    }

    public async Task<SearchResultDto> SearchAsync(
        string? query,
        string? path,
        bool? recursive,
        int? maxResults,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new FileManagerException(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "Search is disabled.",
                "feature_disabled");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "A search query is required.",
                "invalid_query");
        }

        var limit = Math.Clamp(maxResults ?? _options.MaxResults, 1, _options.MaxResults);
        var includeSubdirectories = recursive ?? _options.IncludeSubdirectories;
        var start = _pathGuard.Resolve(path, mustExist: true, allowRoot: true, allowStaging: false);

        if (!Directory.Exists(start.AbsolutePath))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Search path must be a directory.",
                "not_a_directory");
        }

        if (!await _concurrency.WaitAsync(0, cancellationToken))
        {
            throw new FileManagerException(
                StatusCodes.Status429TooManyRequests,
                "Too Many Requests",
                "Too many concurrent searches. Try again shortly.",
                "search_busy");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var results = new List<DirectoryEntryDto>(Math.Min(limit, 128));
            var truncated = false;
            string? truncationReason = null;

            try
            {
                await Task.Run(
                    () => Walk(start.AbsolutePath, query.Trim(), includeSubdirectories, limit, results, timeoutCts.Token, ref truncated, ref truncationReason),
                    timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                truncated = true;
                truncationReason ??= "timeout";
            }

            return new SearchResultDto
            {
                Query = query.Trim(),
                Path = start.VirtualPath,
                Recursive = includeSubdirectories,
                Items = results,
                ResultCount = results.Count,
                MaxResults = limit,
                Truncated = truncated,
                TruncationReason = truncationReason
            };
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private void Walk(
        string directory,
        string query,
        bool recursive,
        int limit,
        List<DirectoryEntryDto> results,
        CancellationToken cancellationToken,
        ref bool truncated,
        ref string? truncationReason)
    {
        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            IEnumerable<FileSystemInfo> infos;
            try
            {
                infos = new DirectoryInfo(current).EnumerateFileSystemInfos();
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogDebug("Skipping inaccessible directory during search.");
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var info in infos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ShouldSkip(info))
                {
                    continue;
                }

                if (info.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(ToEntry(info));
                    if (results.Count >= limit)
                    {
                        truncated = true;
                        truncationReason = "max_results";
                        return;
                    }
                }

                if (recursive && info is DirectoryInfo)
                {
                    pending.Push(info.FullName);
                }
            }
        }
    }

    private bool ShouldSkip(FileSystemInfo info)
    {
        if (string.Equals(info.Name, StorageRoot.StagingFolderName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(info.FullName), _pathGuard.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        if (!_storage.ShowHiddenFiles
            && (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
            return true;
        }

        return false;
    }

    private DirectoryEntryDto ToEntry(FileSystemInfo info)
    {
        var isDir = info is DirectoryInfo;
        long? size = null;
        string? extension = null;
        if (!isDir && info is FileInfo file)
        {
            size = file.Length;
            extension = file.Extension.TrimStart('.');
        }

        return new DirectoryEntryDto
        {
            Name = info.Name,
            Path = _pathGuard.ToVirtualPath(info.FullName),
            IsDirectory = isDir,
            Extension = extension,
            Size = size,
            LastModified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)
        };
    }
}
