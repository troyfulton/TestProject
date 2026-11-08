using System.Text;
using System.Text.RegularExpressions;
using TestProject.Exceptions;

namespace TestProject.Security;

/// <summary>
/// Confines all user-supplied paths to the configured storage root.
/// Rejects traversal, absolute paths, UNC, reserved names, and reparse points.
/// </summary>
public sealed class PathGuard
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly Regex InvalidNamePattern = new(@"[\x00-\x1F]", RegexOptions.Compiled);

    private readonly StorageRoot _storageRoot;

    public PathGuard(StorageRoot storageRoot)
    {
        _storageRoot = storageRoot;
    }

    public string RootPath => _storageRoot.RootPath;

    public string StagingPath => _storageRoot.StagingPath;

    /// <summary>
    /// Resolves a relative virtual path to an absolute path inside the storage root.
    /// </summary>
    /// <param name="relativePath">User-supplied relative path using / or \ separators.</param>
    /// <param name="mustExist">When true, the target must already exist.</param>
    /// <param name="allowRoot">When false, the empty path (storage root) is rejected.</param>
    /// <param name="allowStaging">When false, paths under .uploads are rejected.</param>
    public ResolvedPath Resolve(
        string? relativePath,
        bool mustExist = false,
        bool allowRoot = true,
        bool allowStaging = false)
    {
        var normalizedRelative = NormalizeRelativePath(relativePath);

        if (!allowRoot && string.IsNullOrEmpty(normalizedRelative))
        {
            throw new FileManagerException(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "The storage root cannot be used as the target of this operation.",
                "root_protected");
        }

        ValidatePathSegments(normalizedRelative);

        var combined = string.IsNullOrEmpty(normalizedRelative)
            ? _storageRoot.RootPath
            : Path.GetFullPath(Path.Combine(_storageRoot.RootPath, normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));

        EnsureInsideRoot(combined);

        if (!allowStaging && IsUnderStaging(combined))
        {
            throw new FileManagerException(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "Access to internal upload staging is not allowed.",
                "staging_protected");
        }

        RejectReparsePointsAlongPath(combined);

        if (mustExist && !Directory.Exists(combined) && !File.Exists(combined))
        {
            throw new FileManagerException(
                StatusCodes.Status404NotFound,
                "Not Found",
                "The requested path was not found.",
                "not_found");
        }

        return new ResolvedPath(combined, ToVirtualPath(combined), IsDirectory(combined));
    }

    /// <summary>
    /// Validates a single file or folder name without directory separators.
    /// </summary>
    public void ValidateFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "A file or folder name is required.",
                "invalid_name");
        }

        if (name.Contains('/') || name.Contains('\\') || name.Contains(':'))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "File names cannot contain path separators.",
                "invalid_name");
        }

        ValidateSegment(name);
    }

    public string CombineRelative(string? directoryRelative, string fileName)
    {
        ValidateFileName(fileName);
        var dir = NormalizeRelativePath(directoryRelative);
        return string.IsNullOrEmpty(dir) ? fileName : $"{dir}/{fileName}";
    }

    public string ToVirtualPath(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        EnsureInsideRoot(full);

        if (string.Equals(TrimTrailingSeparators(full), _storageRoot.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var relative = Path.GetRelativePath(_storageRoot.RootPath, full);
        return relative.Replace('\\', '/');
    }

    public bool IsUnderStaging(string absolutePath)
    {
        var full = TrimTrailingSeparators(Path.GetFullPath(absolutePath));
        var staging = TrimTrailingSeparators(_storageRoot.StagingPath);
        return string.Equals(full, staging, StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(staging + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsSameOrDescendant(string candidateAbsolute, string ancestorAbsolute)
    {
        var candidate = TrimTrailingSeparators(Path.GetFullPath(candidateAbsolute));
        var ancestor = TrimTrailingSeparators(Path.GetFullPath(ancestorAbsolute));
        return string.Equals(candidate, ancestor, StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(ancestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(ancestor + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public void EnsureNotSelfOrDescendant(string sourceAbsolute, string destinationAbsolute)
    {
        if (IsSameOrDescendant(destinationAbsolute, sourceAbsolute))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Cannot move or copy a folder into itself or one of its descendants.",
                "invalid_destination");
        }
    }

    private string NormalizeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var trimmed = relativePath.Trim().Replace('\\', '/');

        // Reject absolute, drive-prefixed, and UNC inputs supplied as "relative" paths.
        if (Path.IsPathRooted(trimmed)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("\\\\", StringComparison.Ordinal)
            || Regex.IsMatch(trimmed, @"^[A-Za-z]:"))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "Absolute, drive-prefixed, and UNC paths are not allowed.",
                "invalid_path");
        }

        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (part is "." or "..")
            {
                throw new FileManagerException(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Path traversal segments are not allowed.",
                    "path_traversal");
            }

            if (builder.Length > 0)
            {
                builder.Append('/');
            }

            builder.Append(part);
        }

        return builder.ToString();
    }

    private void ValidatePathSegments(string normalizedRelative)
    {
        if (string.IsNullOrEmpty(normalizedRelative))
        {
            return;
        }

        foreach (var segment in normalizedRelative.Split('/'))
        {
            ValidateSegment(segment);
        }
    }

    private static void ValidateSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)
            || segment is "." or ".."
            || segment.EndsWith('.')
            || segment.EndsWith(' ')
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || InvalidNamePattern.IsMatch(segment))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "The path contains an invalid file or folder name.",
                "invalid_name");
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(segment);
        if (ReservedNames.Contains(segment) || ReservedNames.Contains(nameWithoutExtension))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "The path contains a reserved Windows device name.",
                "reserved_name");
        }
    }

    private void EnsureInsideRoot(string absolutePath)
    {
        var full = TrimTrailingSeparators(Path.GetFullPath(absolutePath));
        var root = _storageRoot.RootPath;

        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Separator-bounded prefix check prevents FileData vs FileData2 collisions.
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileManagerException(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "The path is outside the configured storage root.",
                "path_outside_root");
        }
    }

    /// <summary>
    /// Walks every existing path component and rejects reparse points so traversal
    /// cannot escape through junctions or symbolic links.
    /// </summary>
    private void RejectReparsePointsAlongPath(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        var root = _storageRoot.RootPath;
        var relative = Path.GetRelativePath(root, full);

        if (relative == ".")
        {
            RejectIfReparsePoint(root);
            return;
        }

        RejectIfReparsePoint(root);

        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(segment) || segment == ".")
            {
                continue;
            }

            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                // Remaining segments are for a path being created; stop checking.
                break;
            }

            RejectIfReparsePoint(current);
        }
    }

    private static void RejectIfReparsePoint(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                throw new FileManagerException(
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "Symbolic links and junctions are not allowed in the storage tree.",
                    "reparse_point");
            }
        }
        catch (FileNotFoundException)
        {
            // Path does not exist yet.
        }
        catch (DirectoryNotFoundException)
        {
            // Parent missing.
        }
    }

    private static bool IsDirectory(string path) =>
        Directory.Exists(path) || (!File.Exists(path) && path.EndsWith(Path.DirectorySeparatorChar));

    private static string TrimTrailingSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

/// <summary>
/// Result of resolving a user path against the storage root.
/// </summary>
public sealed record ResolvedPath(string AbsolutePath, string VirtualPath, bool IsDirectory);
