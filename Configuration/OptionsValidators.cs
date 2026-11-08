using Microsoft.Extensions.Options;

namespace TestProject.Configuration;

/// <summary>
/// Startup validation for strongly typed options. Rejects incompatible or unsafe settings.
/// </summary>
public sealed class FileStorageOptionsValidator : IValidateOptions<FileStorageOptions>
{
    private readonly IWebHostEnvironment _environment;

    public FileStorageOptionsValidator(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, FileStorageOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            failures.Add("FileStorage:RootPath is required.");
        }

        if (options.MaxFileSizeGB is < 1 or > 100)
        {
            failures.Add("FileStorage:MaxFileSizeGB must be between 1 and 100.");
        }

        if (options.StreamBufferKB is < 4 or > 1024)
        {
            failures.Add("FileStorage:StreamBufferKB must be between 4 and 1024.");
        }

        if (!string.IsNullOrWhiteSpace(options.RootPath))
        {
            var resolved = Path.IsPathRooted(options.RootPath)
                ? Path.GetFullPath(options.RootPath)
                : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, options.RootPath));

            var wwwroot = Path.GetFullPath(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"));
            var contentRoot = Path.GetFullPath(_environment.ContentRootPath);

            if (IsSameOrUnder(resolved, wwwroot))
            {
                failures.Add("FileStorage:RootPath must not be inside wwwroot.");
            }

            // Reject storing data inside the published application tree other than an explicit sibling under content root.
            if (IsSameOrUnder(resolved, Path.Combine(contentRoot, "bin")) ||
                IsSameOrUnder(resolved, Path.Combine(contentRoot, "obj")))
            {
                failures.Add("FileStorage:RootPath must not be inside bin or obj.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSameOrUnder(string candidate, string parent)
    {
        var c = TrimTrailingSeparators(candidate);
        var p = TrimTrailingSeparators(parent);
        return string.Equals(c, p, StringComparison.OrdinalIgnoreCase)
               || c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || c.StartsWith(p + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

public sealed class BrowsingOptionsValidator : IValidateOptions<BrowsingOptions>
{
    public ValidateOptionsResult Validate(string? name, BrowsingOptions options)
    {
        var failures = new List<string>();

        if (options.DefaultPageSize is < 1 or > 500)
        {
            failures.Add("Browsing:DefaultPageSize must be between 1 and 500.");
        }

        if (options.MaxPageSize is < 1 or > 500)
        {
            failures.Add("Browsing:MaxPageSize must be between 1 and 500.");
        }

        if (options.DefaultPageSize > options.MaxPageSize)
        {
            failures.Add("Browsing:DefaultPageSize cannot exceed MaxPageSize.");
        }

        if (options.MaxFilesForSizeTotal is < 1 or > 100_000)
        {
            failures.Add("Browsing:MaxFilesForSizeTotal must be between 1 and 100000.");
        }

        var sort = options.DefaultSort?.Trim();
        if (!string.Equals(sort, "Name", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sort, "Size", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sort, "LastModified", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Browsing:DefaultSort must be Name, Size, or LastModified.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class SearchOptionsValidator : IValidateOptions<SearchOptions>
{
    public ValidateOptionsResult Validate(string? name, SearchOptions options)
    {
        var failures = new List<string>();

        if (options.MaxResults is < 1 or > 10_000)
        {
            failures.Add("Search:MaxResults must be between 1 and 10000.");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            failures.Add("Search:TimeoutSeconds must be between 1 and 300.");
        }

        if (options.MaxConcurrentSearches is < 1 or > 32)
        {
            failures.Add("Search:MaxConcurrentSearches must be between 1 and 32.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed class UploadsOptionsValidator : IValidateOptions<UploadsOptions>
{
    public ValidateOptionsResult Validate(string? name, UploadsOptions options)
    {
        var failures = new List<string>();

        if (options.MaxFileSizeGB is < 1 or > 100)
        {
            failures.Add("Uploads:MaxFileSizeGB must be between 1 and 100.");
        }

        if (options.ChunkSizeMB is < 1 or > UploadsOptions.MaxAllowedChunkSizeMB)
        {
            failures.Add($"Uploads:ChunkSizeMB must be between 1 and {UploadsOptions.MaxAllowedChunkSizeMB} (IIS request size ceiling).");
        }

        if (options.StreamBufferKB is < 4 or > 1024)
        {
            failures.Add("Uploads:StreamBufferKB must be between 4 and 1024.");
        }

        if (options.MaxConcurrentUploads is < 1 or > 32)
        {
            failures.Add("Uploads:MaxConcurrentUploads must be between 1 and 32.");
        }

        if (options.MaxRetries is < 0 or > 20)
        {
            failures.Add("Uploads:MaxRetries must be between 0 and 20.");
        }

        if (options.RetryInitialDelaySeconds is < 1 or > 60)
        {
            failures.Add("Uploads:RetryInitialDelaySeconds must be between 1 and 60.");
        }

        if (options.SessionExpirationHours is < 1 or > 720)
        {
            failures.Add("Uploads:SessionExpirationHours must be between 1 and 720.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
