using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Settings;
using Platform.Helpers.Constants;

namespace Platform.Auth.Controllers;

/// <summary>
/// The team of the current organization: its people and the invitations still waiting. Everything here needs the
/// <c>members.manage</c> permission, which only the organization admin of a company or an agency holds by default.
/// Responses are marked no-store, because the one that creates an invitation carries its secret.
/// </summary>
[ApiController]
[Route("v1/members")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(AuthRateLimitPolicies.Session)]
[Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MembersManage)]
public sealed class MembersController(ITeamService team) : ControllerBase
{
    /// <summary>The people in the organization and the invitations that are neither accepted nor revoked.</summary>
    [HttpGet]
    [ProducesResponseType<TeamResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TeamResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await team.GetTeamAsync(cancellationToken));

    /// <summary>
    /// Invites an address with a role. The answer holds the secret for the link, <b>once</b>: only its hash is kept, so a
    /// lost link means revoking the invitation and making another. An address that already has an account is refused.
    /// </summary>
    [HttpPost("invitations")]
    [ProducesResponseType<CreatedInvitationResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Invite(InviteMemberRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var created = await team.InviteAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Revokes an invitation that is still waiting; its link stops working.</summary>
    [HttpDelete("invitations/{invitationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeInvitation(Guid invitationId, CancellationToken cancellationToken)
    {
        await team.RevokeInvitationAsync(invitationId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Activates a waiting invitation by hand: the admin gives the default password the person will sign in with. The
    /// person becomes an active member at once and the invitation's link stops working.
    /// </summary>
    [HttpPost("invitations/{invitationId:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ActivateInvitation(Guid invitationId, ActivateInvitationRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        await team.ActivateInvitationAsync(invitationId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Deactivates or activates a member. A deactivated person's sessions end at once and they cannot sign in.</summary>
    [HttpPut("{userId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetStatus(Guid userId, SetMemberActiveRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        await team.SetMemberActiveAsync(userId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Gives a member another role. Their sessions end: they sign in again to get the new role's permissions.</summary>
    [HttpPut("{userId:guid}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangeRole(Guid userId, ChangeMemberRoleRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        await team.ChangeRoleAsync(userId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Removes a member. Their sessions end at once, and their account is deleted when they belong to no other
    /// organization. You cannot remove yourself or the last organization admin.
    /// </summary>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken cancellationToken)
    {
        await team.RemoveMemberAsync(userId, cancellationToken);
        return NoContent();
    }
}
