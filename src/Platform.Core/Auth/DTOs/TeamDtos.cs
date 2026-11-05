using System.Text.Json.Serialization;
using Platform.Helpers.Constants;
using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>Invites a person, by name and email, into the organization with a role.</summary>
public sealed record InviteMemberRequest(string? FullName, string? Email, string? Role)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.RequiredName(errors, "fullName", "Full name", FullName);
        FieldRules.Email(errors, Email);
        FieldRules.RequiredRole(errors, "role", Role);
        return errors;
    }
}

/// <summary>Gives a member another role.</summary>
public sealed record ChangeMemberRoleRequest(string? Role)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.RequiredRole(errors, "role", Role);
        return errors;
    }
}

/// <summary>Deactivates (false) or activates (true) a member. A deactivated person cannot sign in.</summary>
public sealed record SetMemberActiveRequest(bool? IsActive)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        if (IsActive is null)
        {
            errors.Add("isActive", "Say whether the person is active.");
        }

        return errors;
    }
}

/// <summary>
/// Activates a waiting invitation by hand: the admin types the default password the person will sign in with. The
/// invitation's link stops working. The password travels in the body, is hashed at once, and is never logged.
/// </summary>
public sealed record ActivateInvitationRequest(string? Password)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.NewPassword(errors, Password);
        return errors;
    }
}

/// <summary>The secret from an invitation link. It travels in the body, never in an address.</summary>
public sealed record InvitationTokenRequest(string? Token)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.InvitationToken(errors, Token);
        return errors;
    }
}

/// <summary>Accepts an invitation: the person chooses a name and a password, and the address is the invited one.</summary>
public sealed record AcceptInvitationRequest(string? Token, string? FullName, string? Password)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.InvitationToken(errors, Token);
        FieldRules.RequiredName(errors, "fullName", "Full name", FullName);
        FieldRules.NewPassword(errors, Password);
        return errors;
    }
}

public sealed record TeamMemberResponse(
    Guid UserId,
    string Email,
    string FullName,
    [property: JsonConverter(typeof(OrganizationRoleJsonConverter))] OrganizationRole Role,
    DateTimeOffset JoinedAt,
    bool IsYou,
    bool IsActive);

public sealed record InvitationResponse(
    Guid Id,
    string FullName,
    string Email,
    [property: JsonConverter(typeof(OrganizationRoleJsonConverter))] OrganizationRole Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsExpired);

/// <summary>The team page: the people in the organization and the invitations still waiting.</summary>
public sealed record TeamResponse(IReadOnlyList<TeamMemberResponse> Members, IReadOnlyList<InvitationResponse> Invitations);

/// <summary>
/// A new invitation. <paramref name="Token"/> is the secret for the link and is shown only here: the server keeps its
/// hash, so a lost link means revoking the invitation and making another.
/// </summary>
public sealed record CreatedInvitationResponse(InvitationResponse Invitation, string Token);

/// <summary>What the invitation page shows before the person chooses a password.</summary>
public sealed record InvitationPreviewResponse(
    string OrganizationName,
    BusinessModel BusinessModel,
    [property: JsonConverter(typeof(OrganizationRoleJsonConverter))] OrganizationRole Role,
    string Email,
    DateTimeOffset ExpiresAt,
    string FullName);
