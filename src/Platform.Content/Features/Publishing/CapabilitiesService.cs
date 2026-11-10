using Platform.Content.Data;
using Platform.Content.DTOs;
using Platform.Content.Enums;
using Platform.Content.Interfaces;
using Platform.Content.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Tenancy;

namespace Platform.Content.Features.Publishing;

/// <summary>What the Content Studio pages need to know before they draw: what is real, and each platform's limits and whether the organization may use it.</summary>
public sealed class CapabilitiesService(
    IContentMediaLibrary media,
    IContentAiAssistant ai,
    ICampaignLookup campaigns,
    IPublisherCatalog catalog,
    IContentLookupRepository lookups,
    ITenantContext tenant) : ICapabilitiesService
{
    public CapabilitiesResponse GetCapabilities()
    {
        ContentAccess.Require(tenant, PermissionKeys.ContentStudioView, "see content");
        return new CapabilitiesResponse(media.IsAvailable, ai.IsAvailable, campaigns.IsAvailable, catalog.Mode);
    }

    public async Task<IReadOnlyList<PlatformRuleResponse>> GetPlatformRulesAsync(CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioView, "see content");
        var enabled = await lookups.EnabledPlatformsAsync(agencyId, userId, cancellationToken);

        return PlatformRules.All
            .Select(rule => new PlatformRuleResponse(
                rule.Platform.ToWire(), rule.Name, enabled.Contains(rule.Platform.ToWire()), rule.CaptionLimit, rule.TitleLimit, rule.MaxHashtags))
            .ToList();
    }
}
