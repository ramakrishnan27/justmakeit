using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Content.Interfaces;
using Platform.Content.Settings;

namespace Platform.Content.Features.Publishing;

/// <summary>
/// Looks for what is due on a timer and hands it to <see cref="IPublishingService"/>. Runs inside the API process (there is no
/// separate worker). A failed look is logged and tried again at the next tick: nothing a look does may stop the application.
/// </summary>
public sealed class ContentPublishingWorker(
    IServiceScopeFactory scopes, IOptions<ContentOptions> options, ILogger<ContentPublishingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Publishing;
        if (!settings.WorkerEnabled)
        {
            logger.LogInformation("The content publishing worker is turned off (Content:Publishing:WorkerEnabled).");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(settings.StartupDelaySeconds), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.PollSeconds));
            do
            {
                await LookOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task LookOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handled = await scope.ServiceProvider.GetRequiredService<IPublishingService>().ProcessDueAsync(stoppingToken);
            if (handled > 0)
            {
                logger.LogInformation("Handled {Count} content publication(s).", handled);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Looking for content to publish failed; trying again at the next tick.");
        }
    }
}
