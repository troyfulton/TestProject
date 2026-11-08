using Microsoft.Extensions.Options;
using TestProject.Configuration;

namespace TestProject.Security;

/// <summary>
/// Resolves and caches the canonical storage root path for the application lifetime.
/// </summary>
public sealed class StorageRoot
{
    public const string StagingFolderName = ".uploads";

    public const string DefaultHomeFolderName = "home";

    private readonly IWebHostEnvironment _environment;
    private readonly FileStorageOptions _options;
    private readonly ILogger<StorageRoot> _logger;
    private string? _resolvedRoot;

    public StorageRoot(
        IWebHostEnvironment environment,
        IOptions<FileStorageOptions> options,
        ILogger<StorageRoot> logger)
    {
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Canonical absolute path of the configured storage root.
    /// </summary>
    public string RootPath
    {
        get
        {
            if (_resolvedRoot is not null)
            {
                return _resolvedRoot;
            }

            var configured = _options.RootPath.Trim();
            var absolute = Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));

            _resolvedRoot = TrimTrailingSeparators(absolute);
            return _resolvedRoot;
        }
    }

    /// <summary>
    /// Internal staging directory for resumable uploads (outside public web content).
    /// </summary>
    public string StagingPath => Path.Combine(RootPath, StagingFolderName);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(StagingPath);
        SeedDefaultHomeIfEmpty();
    }

    /// <summary>
    /// When FileData has no user folders (ignoring .uploads), create home/readme.md
    /// so a fresh install shows content in the UI.
    /// </summary>
    private void SeedDefaultHomeIfEmpty()
    {
        if (!_options.SeedDefaultHomeWhenEmpty)
        {
            return;
        }

        var hasUserFolder = Directory.EnumerateDirectories(RootPath)
            .Any(path => !string.Equals(
                Path.GetFileName(path),
                StagingFolderName,
                StringComparison.OrdinalIgnoreCase));

        if (hasUserFolder)
        {
            return;
        }

        var homePath = Path.Combine(RootPath, DefaultHomeFolderName);
        Directory.CreateDirectory(homePath);

        var readmePath = Path.Combine(homePath, "readme.md");
        if (!File.Exists(readmePath))
        {
            File.WriteAllText(readmePath, "Hello World");
        }

        _logger.LogInformation(
            "Seeded default folder {Folder} with readme.md because the storage root had no user folders.",
            DefaultHomeFolderName);
    }

    private static string TrimTrailingSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
