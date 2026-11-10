using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Services;

/// <summary>
/// The Platform page of a member: every channel and whether the Site admin has enabled it for the member's organization.
/// Read only. The permission is checked here as well as by the endpoint's policy and by the database function, so no other
/// caller of the service can skip it.
/// </summary>
public sealed class PlatformService(IPlatformRepository platforms, ITenantContext tenant) : IPlatformService
{
    public async Task<IReadOnlyList<PlatformResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var (agencyId, userId) = Require();
        var rows = await platforms.ListAsync(agencyId, userId, cancellationToken);
        return rows.Select(c => new PlatformResponse(c.Id, c.Name, c.Description, c.IconKey, c.HasImage, c.IsEnabled)).ToList();
    }

    public async Task<ChannelIconRecord> GetIconAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = Require();
        return await platforms.GetIconAsync(agencyId, userId, channelId, cancellationToken)
               ?? throw new NotFoundException("That platform has no uploaded icon.", "platform_not_found");
    }

    private (Guid AgencyId, Guid UserId) Require()
    {
        var agencyId = tenant.RequireAgencyId();
        var userId = tenant.RequireUserId();

        if (!tenant.HasPermission(PermissionKeys.PlatformsView))
        {
            throw new ForbiddenException("Your role is not allowed to see the platforms.", "insufficient_permission");
        }

        return (agencyId, userId);
    }
}
