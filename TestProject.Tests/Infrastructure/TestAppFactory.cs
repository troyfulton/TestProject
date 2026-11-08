using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TestProject.Tests.Infrastructure;

/// <summary>
/// Hosts the web app against an isolated temporary FileStorage root.
/// </summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    private readonly string _rootPath;
    private readonly Dictionary<string, string?> _extraSettings;

    public TestAppFactory(string rootPath, Dictionary<string, string?>? extraSettings = null)
    {
        _rootPath = rootPath;
        _extraSettings = extraSettings ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["FileStorage:RootPath"] = _rootPath,
                ["FileStorage:SeedDefaultHomeWhenEmpty"] = "false",
                ["Security:AllowAnonymousAccess"] = "true",
                ["Uploads:VerifyChunkChecksum"] = "true",
                ["Uploads:ChunkSizeMB"] = "1",
                ["Uploads:SessionExpirationHours"] = "1"
            };

            foreach (var pair in _extraSettings)
            {
                settings[pair.Key] = pair.Value;
            }

            config.AddInMemoryCollection(settings);
        });
    }
}
