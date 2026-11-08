using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using TestProject.Configuration;
using TestProject.Exceptions;
using TestProject.Models;
using TestProject.Security;

namespace TestProject.Services;

/// <summary>
/// Filesystem browse, download, create, delete, move, and copy operations.
/// </summary>
public sealed class FileService
{
    private readonly PathGuard _pathGuard;
    private readonly FileStorageOptions _storage;
    private readonly BrowsingOptions _browsing;
    private readonly ILogger<FileService> _logger;

    public FileService(
        PathGuard pathGuard,
        IOptions<FileStorageOptions> storage,
        IOptions<BrowsingOptions> browsing,
        ILogger<FileService> logger)
    {
        _pathGuard = pathGuard;
        _storage = storage.Value;
        _browsing = browsing.Value;
        _logger = logger;
    }

    public DirectoryListingDto List(
        string? path,
        int? page,
        int? pageSize,
        string? sort,
        string? direction,
        CancellationToken cancellationToken)
    {
        var resolved = _pathGuard.Resolve(path, mustExist: true, allowRoot: true, allowStaging: false);
        if (!Directory.Exists(resolved.AbsolutePath))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "The path is not a directory.",
                "not_a_directory");
        }

        var currentPage = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? _browsing.DefaultPageSize, 1, _browsing.MaxPageSize);
        var sortField = NormalizeSort(sort);
        var sortDir = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";

        var entries = new List<FileSystemInfo>();
        foreach (var info in new DirectoryInfo(resolved.AbsolutePath).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldSkip(info))
            {
                continue;
            }

            entries.Add(info);
        }

        var folderCount = entries.Count(e => e is DirectoryInfo);
        var fileEntries = entries.Where(e => e is FileInfo).Cast<FileInfo>().ToList();
        var fileCount = fileEntries.Count;

        long? directorySize = null;
        var directorySizeComplete = true;
        if (fileCount <= _browsing.MaxFilesForSizeTotal)
        {
            long total = 0;
            foreach (var file in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += file.Length;
            }

            directorySize = total;
        }
        else
        {
            directorySizeComplete = false;
        }

        IEnumerable<FileSystemInfo> ordered = sortField switch
        {
            "Size" => sortDir == "desc"
                ? entries.OrderByDescending(SizeKey).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                : entries.OrderBy(SizeKey).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            "LastModified" => sortDir == "desc"
                ? entries.OrderByDescending(e => e.LastWriteTimeUtc).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                : entries.OrderBy(e => e.LastWriteTimeUtc).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            _ => sortDir == "desc"
                ? entries.OrderByDescending(e => e is DirectoryInfo ? 0 : 1)
                    .ThenByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                : entries.OrderByDescending(e => e is DirectoryInfo ? 1 : 0)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        };

        // Directories first for Name sort ascending (folders before files).
        if (sortField == "Name" && sortDir == "asc")
        {
            ordered = entries
                .OrderByDescending(e => e is DirectoryInfo ? 1 : 0)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
        }
        else if (sortField == "Name")
        {
            ordered = entries
                .OrderByDescending(e => e is DirectoryInfo ? 1 : 0)
                .ThenByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase);
        }

        var totalItems = entries.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)size));
        if (currentPage > totalPages)
        {
            currentPage = totalPages;
        }

        var pageItems = ordered.Skip((currentPage - 1) * size).Take(size).ToList();
        var dtos = pageItems.Select(ToEntryDto).ToList();
        var pageFileSize = dtos.Where(d => !d.IsDirectory).Sum(d => d.Size ?? 0);

        string? parentPath = null;
        if (!string.IsNullOrEmpty(resolved.VirtualPath))
        {
            var parent = Path.GetDirectoryName(resolved.VirtualPath.Replace('/', Path.DirectorySeparatorChar));
            parentPath = string.IsNullOrEmpty(parent) ? string.Empty : parent.Replace('\\', '/');
        }

        return new DirectoryListingDto
        {
            Path = resolved.VirtualPath,
            ParentPath = parentPath,
            Items = dtos,
            Page = currentPage,
            PageSize = size,
            Sort = sortField,
            Direction = sortDir,
            PageItemCount = dtos.Count,
            PageFileSizeBytes = pageFileSize,
            DirectoryFolderCount = folderCount,
            DirectoryFileCount = fileCount,
            DirectoryFileSizeBytes = directorySize,
            DirectorySizeComplete = directorySizeComplete,
            TotalItems = totalItems,
            TotalPages = totalPages
        };
    }

    public (string AbsolutePath, string FileName) ResolveDownload(string? path)
    {
        var resolved = _pathGuard.Resolve(path, mustExist: true, allowRoot: false, allowStaging: false);
        if (!File.Exists(resolved.AbsolutePath))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Only files can be downloaded.",
                "not_a_file");
        }

        return (resolved.AbsolutePath, Path.GetFileName(resolved.AbsolutePath));
    }

    public DirectoryEntryDto CreateFolder(string? parentPath, string? name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pathGuard.ValidateFileName(name);

        var parent = _pathGuard.Resolve(parentPath, mustExist: true, allowRoot: true, allowStaging: false);
        if (!Directory.Exists(parent.AbsolutePath))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Parent path is not a directory.",
                "not_a_directory");
        }

        var virtualChild = _pathGuard.CombineRelative(parent.VirtualPath, name!);
        var child = _pathGuard.Resolve(virtualChild, mustExist: false, allowRoot: false, allowStaging: false);

        if (Directory.Exists(child.AbsolutePath) || File.Exists(child.AbsolutePath))
        {
            throw new FileManagerException(
                StatusCodes.Status409Conflict,
                "Conflict",
                "A file or folder with that name already exists.",
                "already_exists");
        }

        Directory.CreateDirectory(child.AbsolutePath);
        _logger.LogInformation("Created folder {Path}", child.VirtualPath);
        return ToEntryDto(new DirectoryInfo(child.AbsolutePath));
    }

    public void Delete(string? path, bool recursive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = _pathGuard.Resolve(path, mustExist: true, allowRoot: false, allowStaging: false);

        if (Directory.Exists(resolved.AbsolutePath))
        {
            var dir = new DirectoryInfo(resolved.AbsolutePath);
            var hasChildren = dir.EnumerateFileSystemInfos().Any();
            if (hasChildren && !recursive)
            {
                throw new FileManagerException(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Directory is not empty. Set recursive=true to delete nested content.",
                    "directory_not_empty");
            }

            try
            {
                Directory.Delete(resolved.AbsolutePath, recursive);
            }
            catch (IOException)
            {
                throw new FileManagerException(
                    StatusCodes.Status409Conflict,
                    "Conflict",
                    "The folder could not be deleted because it is in use or locked.",
                    "locked");
            }
            catch (UnauthorizedAccessException)
            {
                throw new FileManagerException(
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "The folder could not be deleted due to filesystem permissions.",
                    "access_denied");
            }
        }
        else if (File.Exists(resolved.AbsolutePath))
        {
            try
            {
                File.Delete(resolved.AbsolutePath);
            }
            catch (IOException)
            {
                throw new FileManagerException(
                    StatusCodes.Status409Conflict,
                    "Conflict",
                    "The file could not be deleted because it is in use or locked.",
                    "locked");
            }
            catch (UnauthorizedAccessException)
            {
                throw new FileManagerException(
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "The file could not be deleted due to filesystem permissions.",
                    "access_denied");
            }
        }

        _logger.LogInformation("Deleted {Path}", resolved.VirtualPath);
    }

    public DirectoryEntryDto Move(string? sourcePath, string? destinationDirectory, string? destinationName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = _pathGuard.Resolve(sourcePath, mustExist: true, allowRoot: false, allowStaging: false);
        var destDir = _pathGuard.Resolve(destinationDirectory, mustExist: true, allowRoot: true, allowStaging: false);

        if (!Directory.Exists(destDir.AbsolutePath))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Destination is not a directory.",
                "not_a_directory");
        }

        var name = string.IsNullOrWhiteSpace(destinationName)
            ? Path.GetFileName(source.AbsolutePath)
            : destinationName;

        _pathGuard.ValidateFileName(name);
        var destVirtual = _pathGuard.CombineRelative(destDir.VirtualPath, name!);
        var destination = _pathGuard.Resolve(destVirtual, mustExist: false, allowRoot: false, allowStaging: false);

        if (Directory.Exists(source.AbsolutePath))
        {
            _pathGuard.EnsureNotSelfOrDescendant(source.AbsolutePath, destination.AbsolutePath);
        }

        EnforceOverwritePolicy(destination.AbsolutePath);

        try
        {
            if (Directory.Exists(source.AbsolutePath))
            {
                Directory.Move(source.AbsolutePath, destination.AbsolutePath);
            }
            else
            {
                File.Move(source.AbsolutePath, destination.AbsolutePath, _storage.AllowOverwrite);
            }
        }
        catch (IOException ex) when (IsCrossVolume(ex))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Move across volumes is not supported by this deployment.",
                "cross_volume");
        }
        catch (IOException)
        {
            throw new FileManagerException(
                StatusCodes.Status409Conflict,
                "Conflict",
                "The item could not be moved.",
                "move_failed");
        }

        _logger.LogInformation("Moved {Source} to {Destination}", source.VirtualPath, destination.VirtualPath);
        return File.Exists(destination.AbsolutePath)
            ? ToEntryDto(new FileInfo(destination.AbsolutePath))
            : ToEntryDto(new DirectoryInfo(destination.AbsolutePath));
    }

    public async Task<DirectoryEntryDto> CopyAsync(
        string? sourcePath,
        string? destinationDirectory,
        string? destinationName,
        CancellationToken cancellationToken)
    {
        var source = _pathGuard.Resolve(sourcePath, mustExist: true, allowRoot: false, allowStaging: false);
        var destDir = _pathGuard.Resolve(destinationDirectory, mustExist: true, allowRoot: true, allowStaging: false);

        if (!Directory.Exists(destDir.AbsolutePath))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Destination is not a directory.",
                "not_a_directory");
        }

        var name = string.IsNullOrWhiteSpace(destinationName)
            ? Path.GetFileName(source.AbsolutePath)
            : destinationName;

        _pathGuard.ValidateFileName(name);
        var destVirtual = _pathGuard.CombineRelative(destDir.VirtualPath, name!);
        var destination = _pathGuard.Resolve(destVirtual, mustExist: false, allowRoot: false, allowStaging: false);

        if (Directory.Exists(source.AbsolutePath))
        {
            _pathGuard.EnsureNotSelfOrDescendant(source.AbsolutePath, destination.AbsolutePath);
            await CopyDirectoryAsync(source.AbsolutePath, destination.AbsolutePath, cancellationToken);
        }
        else
        {
            EnforceOverwritePolicy(destination.AbsolutePath);
            await CopyFileAsync(source.AbsolutePath, destination.AbsolutePath, cancellationToken);
        }

        _logger.LogInformation("Copied {Source} to {Destination}", source.VirtualPath, destination.VirtualPath);
        return File.Exists(destination.AbsolutePath)
            ? ToEntryDto(new FileInfo(destination.AbsolutePath))
            : ToEntryDto(new DirectoryInfo(destination.AbsolutePath));
    }

    private async Task CopyDirectoryAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
    {
        if (Directory.Exists(destDir) || File.Exists(destDir))
        {
            if (!_storage.AllowOverwrite)
            {
                throw new FileManagerException(
                    StatusCodes.Status409Conflict,
                    "Conflict",
                    "A file or folder with that name already exists.",
                    "already_exists");
            }
        }

        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparseOrStaging(file);
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            await CopyFileAsync(file, destFile, cancellationToken);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparseOrStaging(directory);
            var destSub = Path.Combine(destDir, Path.GetFileName(directory));
            await CopyDirectoryAsync(directory, destSub, cancellationToken);
        }
    }

    private async Task CopyFileAsync(string sourceFile, string destFile, CancellationToken cancellationToken)
    {
        EnforceOverwritePolicy(destFile);

        var bufferSize = Math.Max(4096, _storage.StreamBufferKB * 1024);
        await using var source = new FileStream(
            sourceFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var destination = new FileStream(
            destFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(destination, bufferSize, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private void EnforceOverwritePolicy(string destinationAbsolute)
    {
        if (File.Exists(destinationAbsolute) || Directory.Exists(destinationAbsolute))
        {
            if (!_storage.AllowOverwrite)
            {
                throw new FileManagerException(
                    StatusCodes.Status409Conflict,
                    "Conflict",
                    "A file or folder with that name already exists.",
                    "already_exists");
            }

            if (Directory.Exists(destinationAbsolute))
            {
                throw new FileManagerException(
                    StatusCodes.Status409Conflict,
                    "Conflict",
                    "Cannot overwrite an existing directory in place.",
                    "directory_exists");
            }
        }
    }

    private void RejectReparseOrStaging(string absolutePath)
    {
        if (_pathGuard.IsUnderStaging(absolutePath))
        {
            throw new FileManagerException(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "Access to internal upload staging is not allowed.",
                "staging_protected");
        }

        var attrs = File.GetAttributes(absolutePath);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            throw new FileManagerException(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "Symbolic links and junctions are not allowed in the storage tree.",
                "reparse_point");
        }
    }

    private bool ShouldSkip(FileSystemInfo info)
    {
        if (string.Equals(info.Name, StorageRoot.StagingFolderName, StringComparison.OrdinalIgnoreCase)
            && _pathGuard.IsUnderStaging(info.FullName))
        {
            return true;
        }

        if (string.Equals(info.Name, StorageRoot.StagingFolderName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                Path.GetDirectoryName(info.FullName),
                _pathGuard.RootPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!_storage.ShowHiddenFiles)
        {
            if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                return true;
            }
        }

        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        return false;
    }

    private DirectoryEntryDto ToEntryDto(FileSystemInfo info)
    {
        var isDir = info is DirectoryInfo;
        long? size = null;
        string? extension = null;

        if (!isDir && info is FileInfo file)
        {
            size = file.Length;
            extension = file.Extension;
            if (extension.StartsWith('.'))
            {
                extension = extension[1..];
            }
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

    private static long SizeKey(FileSystemInfo info) =>
        info is FileInfo file ? file.Length : -1;

    private string NormalizeSort(string? sort)
    {
        if (string.Equals(sort, "Size", StringComparison.OrdinalIgnoreCase))
        {
            return "Size";
        }

        if (string.Equals(sort, "LastModified", StringComparison.OrdinalIgnoreCase))
        {
            return "LastModified";
        }

        if (string.IsNullOrWhiteSpace(sort))
        {
            return string.Equals(_browsing.DefaultSort, "Size", StringComparison.OrdinalIgnoreCase) ? "Size"
                : string.Equals(_browsing.DefaultSort, "LastModified", StringComparison.OrdinalIgnoreCase) ? "LastModified"
                : "Name";
        }

        return "Name";
    }

    private static bool IsCrossVolume(IOException ex)
    {
        // Windows ERROR_NOT_SAME_DEVICE = 0x11
        const int NotSameDevice = unchecked((int)0x80070011);
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ex.HResult == NotSameDevice;
    }
}
