using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TestProject.Configuration;
using TestProject.Models;
using TestProject.Tests.Infrastructure;

namespace TestProject.Tests;

public sealed class ConfigAndSecurityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Config_ReturnsPublicSettings_WithoutRootPath()
    {
        using var temp = new TempStorageFixture();
        using var factory = new TestAppFactory(temp.Root);
        using var client = factory.CreateClient();

        var config = await client.GetFromJsonAsync<ClientConfigDto>("/api/config", JsonOptions);
        Assert.NotNull(config);
        Assert.True(config!.Features.EnableUpload);
        Assert.Equal(100, config.Browsing.DefaultPageSize);

        var raw = await client.GetStringAsync("/api/config");
        Assert.DoesNotContain(temp.Root, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RootPath", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledFeature_IsRejectedByApi()
    {
        using var temp = new TempStorageFixture();
        using var factory = new TestAppFactory(temp.Root, new Dictionary<string, string?>
        {
            ["Features:EnableDelete"] = "false"
        });
        using var client = factory.CreateClient();

        File.WriteAllText(Path.Combine(temp.Root, "x.txt"), "x");
        var response = await client.PostAsJsonAsync("/api/files/delete", new DeleteRequest { Path = "x.txt" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousBlocked_WhenSecurityDisallows()
    {
        using var temp = new TempStorageFixture();
        using var factory = new TestAppFactory(temp.Root, new Dictionary<string, string?>
        {
            ["Security:AllowAnonymousAccess"] = "false"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/files");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void OptionsValidator_RejectsInvalidChunkSize()
    {
        var validator = new UploadsOptionsValidator();
        var result = validator.Validate(null, new UploadsOptions { ChunkSizeMB = 64, MaxFileSizeGB = 1 });
        Assert.True(result.Failed);
    }

    [Fact]
    public void EnvironmentVariableOverride_BindsFeatures()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:EnableSearch"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<FeaturesOptions>().Bind(configuration.GetSection(FeaturesOptions.SectionName));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<FeaturesOptions>>().Value;
        Assert.False(options.EnableSearch);
    }
}
