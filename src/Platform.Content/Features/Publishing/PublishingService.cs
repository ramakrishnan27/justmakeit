using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Content.Data;
using Platform.Content.Enums;
using Platform.Content.Features.Posts;
using Platform.Content.Interfaces;
using Platform.Content.Settings;
using Platform.Helpers.Publishing;

namespace Platform.Content.Features.Publishing;

/// <summary>
/// Delivers what is due. A target is claimed (so no other instance takes it), loaded with its post's text, handed to the publisher
/// of its platform, and the outcome is recorded: published, scheduled again after a pause, or failed. A failure is words for a person;
/// no token, no response body and no post text is ever logged or stored in it.
/// </summary>
public sealed class PublishingService(
    IPublicationRepository publications,
    IPublisherCatalog catalog,
    IOptions<ContentOptions> options,
    TimeProvider time,
    ILogger<PublishingService> logger) : IPublishingService
{
    private const int MaxExternalIdLength = 255;
    private const int MaxErrorLength = 2000;

    /// <summary>How long to wait after the first, second and every later failed attempt before trying again.</summary>
    public static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(attempts switch { <= 1 => 1, 2 => 5, _ => 15 });

    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Publishing;
        var claimed = await publications.ClaimDueAsync(settings.BatchSize, settings.StaleMinutes, cancellationToken);

        var handled = 0;
        foreach (var item in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await PublishOneAsync(item, settings, cancellationToken);
                handled++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Left claimed: it is taken again after the stale time, instead of being lost or looped on.
                logger.LogError(exception, "Publishing target {PublicationId} of post {PostId} failed unexpectedly", item.PublicationId, item.PostId);
            }
        }

        return handled;
    }

    private async Task PublishOneAsync(ClaimedPublication item, PublishingOptions settings, CancellationToken cancellationToken)
    {
        var work = await publications.LoadAsync(item.AgencyId, item.PostId, item.PublicationId, cancellationToken);
        if (work is null)
        {
            // Deleted, edited or taken back since it was claimed: nothing to deliver.
            return;
        }

        var target = work.Target;
        var publisher = catalog.Find(target.Platform);
        if (publisher is null)
        {
            var name = DbEnum<SocialPlatform>.TryParse(target.Platform, out var known) ? PlatformRules.For(known).Name : target.Platform;
            await FailAsync(work, target.RetryCount + 1, $"Publishing to {name} is not available yet.", retryable: false, settings, cancellationToken);
            return;
        }

        var request = new SocialPublishRequest
        {
            PublicationId = target.Id,
            AgencyId = work.AgencyId,
            SocialAccountId = target.SocialAccountId,
            Title = target.PlatformTitle ?? (work.Title.Length > 0 ? work.Title : null),
            Text = target.PlatformCaption ?? work.Body,
            Hashtags = work.Hashtags,
            SettingsJson = target.PlatformSettings
        };

        SocialPublishResult result;
        try
        {
            result = await publisher.PublishAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The {Platform} publisher threw for target {PublicationId}", target.Platform, target.Id);
            result = SocialPublishResult.Failure("The platform could not be reached. It will be tried again.", retryable: true);
        }

        if (result.Succeeded)
        {
            var externalId = result.ExternalPostId!.Length > MaxExternalIdLength ? result.ExternalPostId[..MaxExternalIdLength] : result.ExternalPostId;
            var url = PostBuilder.IsWebAddress(result.ExternalPostUrl) ? result.ExternalPostUrl : null;
            await publications.RecordSuccessAsync(work.AgencyId, target.Id, externalId, url, time.GetUtcNow(), cancellationToken);
            logger.LogInformation("Published target {PublicationId} of post {PostId} to {Platform}", target.Id, work.PostId, target.Platform);
            return;
        }

        await FailAsync(work, target.RetryCount + 1, result.Error!, result.Retryable, settings, cancellationToken);
    }

    private async Task FailAsync(
        PublicationWork work, int attempts, string error, bool retryable, PublishingOptions settings, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var retry = retryable && attempts < settings.MaxAttempts;
        DateTimeOffset? retryAt = retry ? now + Backoff(attempts) : null;

        await publications.RecordFailureAsync(work.AgencyId, work.Target.Id, Clean(error), retryAt, now, cancellationToken);
        logger.LogWarning(
            "Target {PublicationId} of post {PostId} to {Platform} failed (attempt {Attempts}); {Outcome}",
            work.Target.Id, work.PostId, work.Target.Platform, attempts, retry ? "it will be tried again" : "it is failed");
    }

    /// <summary>One line of at most 2000 characters: the error is shown to people and must fit the column.</summary>
    private static string Clean(string error)
    {
        var line = string.Concat(error.Select(c => char.IsControl(c) ? ' ' : c)).Trim();
        return line.Length > MaxErrorLength ? line[..MaxErrorLength] : line;
    }
}
