using Microsoft.Extensions.Logging;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Services;

public sealed class PermissionAdminService(
    IPermissionRepository permissions,
    IPermissionService permissionCache,
    ITenantContext tenant,
    ILogger<PermissionAdminService> logger) : IPermissionAdminService
{
    public async Task<PermissionMatrixResponse> GetMatrixAsync(CancellationToken cancellationToken)
    {
        var userId = RequireSiteAdmin();
        var catalog = await permissions.GetCatalogAsync(userId, cancellationToken);

        // Eight slots, in a fixed order: each business model with the roles it allows.
        var slots = Enum.GetValues<BusinessModel>()
            .SelectMany(model => model.AllowedRoles().Select(role => (Model: model, Role: role)))
            .ToList();

        var granted = catalog.Grants
            .Where(grant => grant.IsGranted)
            .Select(grant => (grant.BusinessModel, grant.Role, grant.PermissionKey))
            .ToHashSet();

        var rows = catalog.Permissions
            .Select(permission => new PermissionResponse(
                permission.Key,
                permission.Title,
                permission.Description,
                permission.IsProtected,
                slots.Select(slot => new GrantResponse(
                    slot.Model.ToDb(), slot.Role.ToDb(), granted.Contains((slot.Model.ToDb(), slot.Role.ToDb(), permission.Key)))).ToList()))
            .ToList();

        return new PermissionMatrixResponse(slots.Select(slot => new SlotResponse(slot.Model.ToDb(), slot.Role.ToDb())).ToList(), rows);
    }

    public async Task<PermissionGrantResponse> SetGrantAsync(
        string permissionKey, SetGrantRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var userId = RequireSiteAdmin();

        // Validate() accepted the text, so it names a model and a role.
        BusinessModelRules.TryParse(request.BusinessModel, out var model);
        RolePermissions.TryParse(request.Role, out var role);
        var granted = request.Granted!.Value;

        if (!model.IsAllowed(role))
        {
            throw new BadRequestException("That role does not exist in that kind of organization.", "invalid_slot");
        }

        if (!PermissionKeys.All.Contains(permissionKey))
        {
            throw new NotFoundException("There is no such permission.", "permission_not_found");
        }

        // The database refuses this too (a trigger); saying so here spares it the round trip.
        if (!granted && PermissionKeys.IsProtected(permissionKey))
        {
            throw new ConflictException("This permission is protected: every role keeps it.", "permission_protected");
        }

        bool found;
        try
        {
            found = await permissions.SetGrantAsync(userId, model.ToDb(), role.ToDb(), permissionKey, granted, cancellationToken);
        }
        finally
        {
            // Raised by the trigger already; this makes the next request on this instance see the new
            // version at once instead of within the cache lifetime. After any attempt, committed or not.
            permissionCache.Invalidate();
        }

        if (!found)
        {
            throw new NotFoundException("There is no such permission.", "permission_not_found");
        }

        logger.LogInformation(
            "Site admin {UserId} {Action} permission {Permission} for {BusinessModel} {Role}",
            userId, granted ? "granted" : "revoked", permissionKey, model.ToDb(), role.ToDb());
        return new PermissionGrantResponse(permissionKey, model.ToDb(), role.ToDb(), granted);
    }

    private Guid RequireSiteAdmin()
    {
        var userId = tenant.RequireUserId();
        return tenant.Role == RoleNames.SiteAdmin
            ? userId
            : throw new ForbiddenException("Site admin access is required.", "not_site_admin");
    }
}
