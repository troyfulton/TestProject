using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using TestProject.Configuration;
using TestProject.Middleware;
using TestProject.Security;
using TestProject.Services;

namespace TestProject;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();
        builder.Services.AddProblemDetails();

        builder.Services.AddOptions<FileStorageOptions>()
            .Bind(builder.Configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<FileStorageOptions>, FileStorageOptionsValidator>();

        builder.Services.AddOptions<BrowsingOptions>()
            .Bind(builder.Configuration.GetSection(BrowsingOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<BrowsingOptions>, BrowsingOptionsValidator>();

        builder.Services.AddOptions<SearchOptions>()
            .Bind(builder.Configuration.GetSection(SearchOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<SearchOptions>, SearchOptionsValidator>();

        builder.Services.AddOptions<UploadsOptions>()
            .Bind(builder.Configuration.GetSection(UploadsOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<UploadsOptions>, UploadsOptionsValidator>();

        builder.Services.AddOptions<FeaturesOptions>()
            .Bind(builder.Configuration.GetSection(FeaturesOptions.SectionName));

        builder.Services.AddOptions<SecurityOptions>()
            .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName));

        builder.Services.AddSingleton<StorageRoot>();
        builder.Services.AddSingleton<PathGuard>();
        builder.Services.AddSingleton<FileService>();
        builder.Services.AddSingleton<DemoDataService>();
        builder.Services.AddSingleton<SearchService>();
        builder.Services.AddSingleton<UploadService>();
        builder.Services.AddHostedService<UploadCleanupService>();

        // Chunk uploads are small relative to 100 GB files; raise body limits to the IIS web.config ceiling.
        const long maxChunkRequestBytes = 64L * 1024L * 1024L;
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = maxChunkRequestBytes;
        });
        builder.Services.Configure<IISServerOptions>(options =>
        {
            options.MaxRequestBodySize = maxChunkRequestBytes;
        });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = maxChunkRequestBytes;
            // Slow networks uploading large chunks should not be aborted by min data rate.
            options.Limits.MinRequestBodyDataRate = new MinDataRate(
                bytesPerSecond: 100,
                gracePeriod: TimeSpan.FromSeconds(30));
        });

        var app = builder.Build();

        // Create storage root and staging before serving requests.
        var storageRoot = app.Services.GetRequiredService<StorageRoot>();
        storageRoot.EnsureCreated();

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseHttpsRedirection();
        app.UseMiddleware<AccessControlMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();

        // SPA fallback for deep-linkable client routes under the same origin.
        app.MapFallbackToFile("index.html");

        app.Run();
    }
}
