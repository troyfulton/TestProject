using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TestProject.Models;
using TestProject.Tests.Infrastructure;

namespace TestProject.Tests;

public sealed class FileServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly TempStorageFixture _temp = new();
    private readonly TestAppFactory _factory;
    private readonly HttpClient _client;

    public FileServiceTests()
    {
        _factory = new TestAppFactory(_temp.Root);
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task List_EmptyDirectory_ReturnsZeroCounts()
    {
        var listing = await _client.GetFromJsonAsync<DirectoryListingDto>("/api/files", JsonOptions);
        Assert.NotNull(listing);
        Assert.Equal(0, listing!.DirectoryFileCount);
        Assert.Equal(0, listing.DirectoryFolderCount);
        Assert.Empty(listing.Items);
    }

    [Fact]
    public async Task CreateFolder_AndList_Works()
    {
        var response = await _client.PostAsJsonAsync("/api/files/folders", new CreateFolderRequest { Path = "", Name = "Projects" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var listing = await _client.GetFromJsonAsync<DirectoryListingDto>("/api/files", JsonOptions);
        Assert.Contains(listing!.Items, i => i.Name == "Projects" && i.IsDirectory);
    }

    [Fact]
    public async Task Delete_File_RemovesEntry()
    {
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "note.txt"), "hello");
        var response = await _client.PostAsJsonAsync("/api/files/delete", new DeleteRequest { Path = "note.txt", Recursive = false });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(_temp.Root, "note.txt")));
    }

    [Fact]
    public async Task Delete_NonEmptyFolder_RequiresRecursive()
    {
        var dir = Path.Combine(_temp.Root, "nested");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "a.txt"), "a");

        var blocked = await _client.PostAsJsonAsync("/api/files/delete", new DeleteRequest { Path = "nested", Recursive = false });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var deleted = await _client.PostAsJsonAsync("/api/files/delete", new DeleteRequest { Path = "nested", Recursive = true });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Copy_AndMove_Files()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Root, "src"));
        Directory.CreateDirectory(Path.Combine(_temp.Root, "dst"));
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "src", "data.txt"), "payload");

        var copy = await _client.PostAsJsonAsync("/api/files/copy", new MoveCopyRequest
        {
            SourcePath = "src/data.txt",
            DestinationDirectory = "dst",
            DestinationName = "data-copy.txt"
        });
        Assert.Equal(HttpStatusCode.OK, copy.StatusCode);
        Assert.True(File.Exists(Path.Combine(_temp.Root, "dst", "data-copy.txt")));

        var move = await _client.PostAsJsonAsync("/api/files/move", new MoveCopyRequest
        {
            SourcePath = "dst/data-copy.txt",
            DestinationDirectory = "src",
            DestinationName = "moved.txt"
        });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.True(File.Exists(Path.Combine(_temp.Root, "src", "moved.txt")));
        Assert.False(File.Exists(Path.Combine(_temp.Root, "dst", "data-copy.txt")));
    }

    [Fact]
    public async Task Copy_RejectsSelfDescendant()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Root, "tree", "child"));
        var response = await _client.PostAsJsonAsync("/api/files/copy", new MoveCopyRequest
        {
            SourcePath = "tree",
            DestinationDirectory = "tree/child",
            DestinationName = "oops"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Conflict_OnExistingDestination()
    {
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "b.txt"), "b");
        Directory.CreateDirectory(Path.Combine(_temp.Root, "folder"));

        var response = await _client.PostAsJsonAsync("/api/files/copy", new MoveCopyRequest
        {
            SourcePath = "a.txt",
            DestinationDirectory = "",
            DestinationName = "b.txt"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task MissingFile_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/files?path=missing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_StreamsFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_temp.Root, "dl.txt"), "download-me");
        var response = await _client.GetAsync("/api/files/download?path=dl.txt");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal("download-me", text);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _temp.Dispose();
    }
}
