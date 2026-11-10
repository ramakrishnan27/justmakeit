using Platform.Content.Interfaces;

namespace Platform.Content.Features.Campaigns;

/// <summary>There are no campaigns yet, so no campaign exists and a post cannot belong to one. Replaced when campaigns exist.</summary>
public sealed class NoCampaigns : ICampaignLookup
{
    public bool IsAvailable => false;

    public Task<bool> ExistsAsync(Guid agencyId, Guid userId, Guid campaignId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
