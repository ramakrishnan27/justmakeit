using Microsoft.Extensions.Logging;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Validation;

namespace Platform.Auth.Services;

/// <summary>The Site admin's channels: the list, creating one (with an uploaded icon), and which organizations have which.</summary>
public sealed class SiteChannelService(
    ISiteChannelRepository channels,
    ITenantContext tenant,
    ILogger<SiteChannelService> logger) : ISiteChannelService
{
    public async Task<IReadOnlyList<SiteChannelResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = SiteAdminGuard.Require(tenant);
        var rows = await channels.ListAsync(userId, cancellationToken);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<SiteChannelResponse> CreateAsync(
        string? name, string? description, byte[]? image, string? declaredContentType, CancellationToken cancellationToken)
    {
        var userId = SiteAdminGuard.Require(tenant);

        var errors = new ValidationErrors();
        FieldRules.ChannelName(errors, "name", name);
        FieldRules.ChannelDescription(errors, "description", description);

        string? contentType = null;
        if (image is not null)
        {
            var problem = ChannelIconRules.Problem(image, declaredContentType, out var found);
            if (problem is null)
            {
                contentType = found;
            }
            else
            {
                errors.Add("icon", problem);
            }
        }

        errors.ThrowIfAny();

        // A channel has a built-in glyph or an uploaded image, never both: with no image it gets the default glyph.
        var channel = new NewChannel(
            name!.Trim(),
            description?.Trim() ?? string.Empty,
            contentType is null ? ChannelIconRules.DefaultGlyph : null,
            contentType is null ? null : image,
            contentType);

        var created = await channels.CreateAsync(userId, channel, cancellationToken);

        // Ids only: never the image.
        logger.LogInformation("Site admin {UserId} created channel {ChannelId}", userId, created.Id);
        return ToResponse(created);
    }

    public async Task<ChannelIconRecord> GetIconAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var userId = SiteAdminGuard.Require(tenant);
        return await channels.GetIconAsync(userId, channelId, cancellationToken)
               ?? throw new NotFoundException("That channel has no uploaded icon.", "channel_not_found");
    }

    public async Task<IReadOnlyList<OrganizationChannelResponse>> ListForOrganizationAsync(Guid agencyId, CancellationToken cancellationToken)
    {
        var userId = SiteAdminGuard.Require(tenant);
        var rows = await channels.ListForOrganizationAsync(userId, agencyId, cancellationToken);
        return rows
            .Select(c => new OrganizationChannelResponse(c.Id, c.Name, c.Description, c.IconKey, c.HasImage, c.IsEnabled))
            .ToList();
    }

    public async Task SetForOrganizationAsync(
        Guid agencyId, Guid channelId, SetOrganizationChannelRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var userId = SiteAdminGuard.Require(tenant);

        var enabled = request.Enabled!.Value;
        var changed = await channels.SetForOrganizationAsync(userId, agencyId, channelId, enabled, cancellationToken);

        logger.LogInformation(
            "Site admin {UserId} {Action} channel {ChannelId} for organization {AgencyId} (changed: {Changed})",
            userId, enabled ? "enabled" : "disabled", channelId, agencyId, changed);
    }

    private static SiteChannelResponse ToResponse(SiteChannelRecord c) =>
        new(c.Id, c.Name, c.Description, c.IconKey, c.HasImage, c.CreatedByName, c.CreatedAt, c.OrganizationCount);
}
