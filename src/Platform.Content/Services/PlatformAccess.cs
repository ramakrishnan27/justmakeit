using Platform.Content.Entities;
using Platform.Content.Interfaces;
using Platform.Helpers.Exceptions;

namespace Platform.Content.Services;

/// <summary>Checks shared by the post and scheduling services: that what a post refers to is real and allowed.</summary>
public static class PlatformAccess
{
    /// <summary>
    /// Every platform the post goes to must be enabled for the organization by the Site admin. A person never sees a platform that is
    /// not, but a request can name one, and a platform can be switched off after a draft chose it.
    /// </summary>
    public static async Task EnsureEnabledAsync(
        IContentLookupRepository lookups, Guid agencyId, Guid userId, IReadOnlyCollection<ContentPostPlatform> targets, CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var enabled = await lookups.EnabledPlatformsAsync(agencyId, userId, cancellationToken);
        var refused = targets.Select(t => t.Platform).Where(platform => !enabled.Contains(platform)).ToList();
        if (refused.Count > 0)
        {
            var errors = new Dictionary<string, string[]> { ["platforms"] = [.. refused.Select(p => $"{p} is not enabled for your organization.")] };
            throw new BadRequestException("A platform is not enabled for your organization.", "platform_not_enabled", errors);
        }
    }

    /// <summary>A post can belong to a campaign only where campaigns exist and this one is among them.</summary>
    public static async Task EnsureCampaignAsync(
        ICampaignLookup campaigns, Guid agencyId, Guid userId, Guid? campaignId, CancellationToken cancellationToken)
    {
        if (campaignId is { } id && !await campaigns.ExistsAsync(agencyId, userId, id, cancellationToken))
        {
            var errors = new Dictionary<string, string[]> { ["campaignId"] = ["That campaign is not available."] };
            throw new BadRequestException("That campaign is not available.", "campaign_not_available", errors);
        }
    }
}
