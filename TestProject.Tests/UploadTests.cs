using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TestProject.Models;
using TestProject.Tests.Infrastructure;

namespace TestProject.Tests;

public sealed class UploadTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly TempStorageFixture _temp = new();
    private readonly TestAppFactory _factory;
    private readonly HttpClient _client;

    public UploadTests()
    {
        _factory = new TestAppFactory(_temp.Root);
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Upload_CreateChunkComplete_Succeeds()
    {
        var payload = Encoding.UTF8.GetBytes("hello resumable world");
        var session = await CreateSessionAsync("hello.txt", payload.Length);
        Assert.Equal(0, session.Offset);

        var checksum = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var afterChunk = await PutChunkAsync(session.UploadId, 0, payload, checksum);
        Assert.Equal(payload.Length, afterChunk.Offset);

        var completed = await _client.PostAsJsonAsync($"/api/uploads/{session.UploadId}/complete", new CompleteUploadRequest());
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.True(File.Exists(Path.Combine(_temp.Root, "hello.txt")));
        Assert.Equal("hello resumable world", await File.ReadAllTextAsync(Path.Combine(_temp.Root, "hello.txt")));
    }

    [Fact]
    public async Task Upload_WrongOffset_ReturnsConflict()
    {
        var payload = Encoding.UTF8.GetBytes("abcdef");
        var session = await CreateSessionAsync("offset.txt", payload.Length);
        var checksum = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var response = await PutChunkRawAsync(session.UploadId, 2, payload, checksum);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ChecksumMismatch_ReturnsBadRequest()
    {
        var payload = Encoding.UTF8.GetBytes("checksum");
        var session = await CreateSessionAsync("sum.txt", payload.Length);
        var response = await PutChunkRawAsync(session.UploadId, 0, payload, "00");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var status = await _client.GetFromJsonAsync<UploadSessionDto>($"/api/uploads/{session.UploadId}", JsonOptions);
        Assert.Equal(0, status!.Offset);
    }

    [Fact]
    public async Task Upload_InterruptedChunk_DoesNotAdvanceOffset()
    {
        var payload = Encoding.UTF8.GetBytes("0123456789");
        var session = await CreateSessionAsync("partial.txt", payload.Length);

        // Send only part of the declared remaining bytes; empty follow-up not sent — server accepts partial chunk writes
        // as committed bytes for that request. Simulate failure by sending bad checksum after writing would truncate.
        var first = payload.AsSpan(0, 4).ToArray();
        var bad = await PutChunkRawAsync(session.UploadId, 0, first, "deadbeef");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var status = await _client.GetFromJsonAsync<UploadSessionDto>($"/api/uploads/{session.UploadId}", JsonOptions);
        Assert.Equal(0, status!.Offset);
    }

    [Fact]
    public async Task Upload_ReconcilesAfterRestart()
    {
        var payload = Encoding.UTF8.GetBytes("restart-me-please");
        var session = await CreateSessionAsync("restart.txt", payload.Length);
        var first = payload.AsSpan(0, 8).ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(first)).ToLowerInvariant();
        await PutChunkAsync(session.UploadId, 0, first, checksum);

        // Corrupt metadata offset ahead of file length to force reconciliation.
        var metaPath = Path.Combine(_temp.Root, ".uploads", session.UploadId, "meta.json");
        var json = await File.ReadAllTextAsync(metaPath);
        json = json.Replace("\"offset\":8", "\"offset\":12");
        await File.WriteAllTextAsync(metaPath, json);

        var status = await _client.GetFromJsonAsync<UploadSessionDto>($"/api/uploads/{session.UploadId}", JsonOptions);
        Assert.Equal(8, status!.Offset);
    }

    [Fact]
    public async Task Upload_FinalLengthMismatch_FailsComplete()
    {
        var session = await CreateSessionAsync("short.txt", 10);
        var bytes = Encoding.UTF8.GetBytes("12345");
        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await PutChunkAsync(session.UploadId, 0, bytes, checksum);

        var response = await _client.PostAsJsonAsync($"/api/uploads/{session.UploadId}/complete", new CompleteUploadRequest());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_DuplicateComplete_IsIdempotent()
    {
        var payload = Encoding.UTF8.GetBytes("dup");
        var session = await CreateSessionAsync("dup.txt", payload.Length);
        var checksum = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        await PutChunkAsync(session.UploadId, 0, payload, checksum);

        var first = await _client.PostAsJsonAsync($"/api/uploads/{session.UploadId}/complete", new CompleteUploadRequest());
        var second = await _client.PostAsJsonAsync($"/api/uploads/{session.UploadId}/complete", new CompleteUploadRequest());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Upload_Cancel_RemovesStaging()
    {
        var session = await CreateSessionAsync("cancel.txt", 4);
        var response = await _client.DeleteAsync($"/api/uploads/{session.UploadId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_temp.Root, ".uploads", session.UploadId)));
    }

    [Fact]
    public async Task Upload_OversizedFile_Rejected()
    {
        var factory = new TestAppFactory(_temp.Root, new Dictionary<string, string?>
        {
            ["Uploads:MaxFileSizeGB"] = "1"
        });
        // Max is still 1GB which is huge; instead create session with negative handled separately.
        // Oversized relative to configured max: temporarily use Create with FileSize beyond options by env.
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/uploads", new CreateUploadRequest
        {
            DestinationDirectory = "",
            FileName = "huge.bin",
            FileSize = 2L * 1024 * 1024 * 1024 // 2 GB > 1 GB
        });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ZeroByteFile_CompletesWithoutChunks()
    {
        var session = await CreateSessionAsync("empty.txt", 0);
        var completed = await _client.PostAsJsonAsync($"/api/uploads/{session.UploadId}/complete", new CompleteUploadRequest());
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.True(File.Exists(Path.Combine(_temp.Root, "empty.txt")));
        Assert.Equal(0, new FileInfo(Path.Combine(_temp.Root, "empty.txt")).Length);
    }

    private async Task<UploadSessionDto> CreateSessionAsync(string fileName, long size)
    {
        var response = await _client.PostAsJsonAsync("/api/uploads", new CreateUploadRequest
        {
            DestinationDirectory = "",
            FileName = fileName,
            FileSize = size
        });
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<UploadSessionDto>(JsonOptions);
        return session!;
    }

    private async Task<UploadSessionDto> PutChunkAsync(string uploadId, long offset, byte[] payload, string checksum)
    {
        var response = await PutChunkRawAsync(uploadId, offset, payload, checksum);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UploadSessionDto>(JsonOptions))!;
    }

    private async Task<HttpResponseMessage> PutChunkRawAsync(string uploadId, long offset, byte[] payload, string checksum)
    {
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/uploads/{uploadId}/chunk?offset={offset}")
        {
            Content = content
        };
        request.Headers.Add("X-Chunk-Checksum", checksum);
        return await _client.SendAsync(request);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _temp.Dispose();
    }
}
