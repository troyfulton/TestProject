namespace TestProject.Services;

/// <summary>
/// Periodically removes expired and completed upload staging sessions.
/// </summary>
public sealed class UploadCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<UploadCleanupService> _logger;

    public UploadCleanupService(IServiceProvider services, ILogger<UploadCleanupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var uploads = scope.ServiceProvider.GetRequiredService<UploadService>();
                await uploads.CleanupExpiredSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Upload cleanup pass failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
