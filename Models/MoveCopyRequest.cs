namespace TestProject.Models;

public sealed class MoveCopyRequest
{
    public string? SourcePath { get; set; }

    public string? DestinationDirectory { get; set; }

    /// <summary>
    /// Optional new name. Defaults to the source name.
    /// </summary>
    public string? DestinationName { get; set; }
}
