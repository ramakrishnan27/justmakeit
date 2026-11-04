using System.Text.Json.Serialization;
using Platform.Helpers.Constants;
using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>Invites a person by email into the organization with a role.</summary>
public sealed record InviteMemberRequest(string? Email, string? Role)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
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
    bool IsYou);

public sealed record InvitationResponse(
    Guid Id,
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
    DateTimeOffset ExpiresAt);
