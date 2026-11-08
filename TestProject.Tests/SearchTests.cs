using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TestProject.Models;
using TestProject.Tests.Infrastructure;

namespace TestProject.Tests;

public sealed class SearchTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly TempStorageFixture _temp = new();
    private readonly TestAppFactory _factory;
    private readonly HttpClient _client;

    public SearchTests()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Root, "docs"));
        File.WriteAllText(Path.Combine(_temp.Root, "docs", "report.txt"), "x");
        File.WriteAllText(Path.Combine(_temp.Root, "notes.md"), "y");
        Directory.CreateDirectory(Path.Combine(_temp.Root, "Reports"));

        _factory = new TestAppFactory(_temp.Root, new Dictionary<string, string?>
        {
            ["Search:MaxResults"] = "2"
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Search_MatchesFileNames_CaseInsensitive()
    {
        var result = await _client.GetFromJsonAsync<SearchResultDto>("/api/search?q=report", JsonOptions);
        Assert.NotNull(result);
        Assert.Contains(result!.Items, i => i.Name.Equals("report.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Items, i => i.Name.Equals("Reports", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_RespectsMaxResults()
    {
        File.WriteAllText(Path.Combine(_temp.Root, "report-a.txt"), "a");
        File.WriteAllText(Path.Combine(_temp.Root, "report-b.txt"), "b");
        File.WriteAllText(Path.Combine(_temp.Root, "report-c.txt"), "c");

        var result = await _client.GetFromJsonAsync<SearchResultDto>("/api/search?q=report", JsonOptions);
        Assert.True(result!.Truncated);
        Assert.Equal(2, result.ResultCount);
        Assert.Equal("max_results", result.TruncationReason);
    }

    [Fact]
    public async Task Search_InvalidPath_ReturnsError()
    {
        var response = await _client.GetAsync("/api/search?q=x&path=nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Search_CanBeCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _client.GetAsync("/api/search?q=report", cts.Token);
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _temp.Dispose();
    }
}
