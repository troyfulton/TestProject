namespace TestProject.Configuration;

/// <summary>
/// Feature flags enforced by the API and reflected in public client config.
/// </summary>
public sealed class FeaturesOptions
{
    public const string SectionName = "Features";

    public bool EnableUpload { get; set; } = true;

    public bool EnableDownload { get; set; } = true;

    public bool EnableSearch { get; set; } = true;

    public bool EnableCreateFolder { get; set; } = true;

    public bool EnableDelete { get; set; } = true;

    public bool EnableMove { get; set; } = true;

    public bool EnableCopy { get; set; } = true;

    public bool EnableDialogMode { get; set; } = true;

    /// <summary>
    /// When true, the header button and POST /api/demo/seed may create the demonstration tree.
    /// Defaults to true so existing appsettings files do not need a new key.
    /// </summary>
    public bool EnableGenerateTestData { get; set; } = true;
}
