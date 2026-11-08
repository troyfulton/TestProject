using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TestProject.Models;
using TestProject.Services;
using TestProject.Tests.Infrastructure;

namespace TestProject.Tests;

public sealed class DemoSeedTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Seed_CreatesHierarchy_AndSkipsExistingFiles()
    {
        using var temp = new TempStorageFixture();
        var marker = Path.Combine(temp.Root, ".uploads", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "staging");

        var readme = Path.Combine(temp.Root, "README-DEMO.txt");
        File.WriteAllText(readme, "KEEP");

        using var factory = new TestAppFactory(temp.Root);
        using var client = factory.CreateClient();

        var first = await client.PostAsync("/api/demo/seed", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var raw = await first.Content.ReadAsStringAsync();
        Assert.DoesNotContain(temp.Root, raw, StringComparison.OrdinalIgnoreCase);
        var created = JsonSerializer.Deserialize<DemoSeedResult>(raw, JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(DemoDataService.ExpectedDirectoryCount, created!.CreatedDirectories);
        Assert.Equal(DemoDataService.ExpectedFileCount - 1, created.CreatedFiles);
        Assert.True(created.SkippedExisting >= 1);

        Assert.Equal("KEEP", File.ReadAllText(readme));
        Assert.Equal("staging", File.ReadAllText(marker));
        Assert.Equal(
            DemoDataService.LargeDirectoryFileCount,
            Directory.GetFiles(Path.Combine(temp.Root, "08-Scale-100x", "Large-Directory-Alpha")).Length);
        Assert.True(File.Exists(Path.Combine(
            temp.Root,
            "08-Scale-100x",
            "Region-100",
            "Sector-074",
            "asset-100-074.pdf")));
        Assert.True(Directory.Exists(Path.Combine(
            temp.Root,
            "02-Rebel-Alliance",
            "Logistics",
            "Supply-Yavin",
            "Test Folder")));
        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Root, "00-Corporate", "Legal-Counsel")));

        var alphaCount = Directory.GetFiles(Path.Combine(temp.Root, "08-Scale-100x", "Large-Directory-Alpha")).Length;
        var second = await client.PostAsync("/api/demo/seed", content: null);
        var again = await second.Content.ReadFromJsonAsync<DemoSeedResult>(JsonOptions);
        Assert.NotNull(again);
        Assert.Equal(0, again!.CreatedDirectories);
        Assert.Equal(0, again.CreatedFiles);
        Assert.True(again.SkippedExisting > created.SkippedExisting);
        Assert.Equal(alphaCount, Directory.GetFiles(Path.Combine(temp.Root, "08-Scale-100x", "Large-Directory-Alpha")).Length);
        Assert.Equal("KEEP", File.ReadAllText(readme));
        Assert.Equal("staging", File.ReadAllText(marker));
    }

    [Fact]
    public async Task Seed_IsForbidden_WhenFeatureDisabled()
    {
        using var temp = new TempStorageFixture();
        using var factory = new TestAppFactory(temp.Root, new Dictionary<string, string?>
        {
            ["Features:EnableGenerateTestData"] = "false"
        });
        using var client = factory.CreateClient();

        var config = await client.GetFromJsonAsync<ClientConfigDto>("/api/config", JsonOptions);
        Assert.NotNull(config);
        Assert.False(config!.Features.EnableGenerateTestData);

        var response = await client.PostAsync("/api/demo/seed", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(temp.Root, "README-DEMO.txt")));
    }

    [Fact]
    public async Task Config_ExposesGenerateTestDataFlag()
    {
        using var temp = new TempStorageFixture();
        using var factory = new TestAppFactory(temp.Root);
        using var client = factory.CreateClient();

        var config = await client.GetFromJsonAsync<ClientConfigDto>("/api/config", JsonOptions);
        Assert.NotNull(config);
        Assert.True(config!.Features.EnableGenerateTestData);

        var raw = await client.GetStringAsync("/api/config");
        Assert.Contains("enableGenerateTestData", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("RootPath", raw, StringComparison.OrdinalIgnoreCase);
    }
}
