namespace TestProject.Tests.Infrastructure;

/// <summary>
/// Creates an isolated temporary storage root for destructive filesystem tests.
/// </summary>
public sealed class TempStorageFixture : IDisposable
{
    public TempStorageFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "MapLargeFileManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for locked files.
        }
    }
}
