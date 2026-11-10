using System.Text.Json.Serialization;
using Platform.Helpers.Constants;

namespace Platform.Auth.DTOs;

/// <summary>
/// One person as the site admin sees them: who they are and the organization they belong to (called an agency in
/// the code). A person in two organizations is two of these.
/// </summary>
public sealed record SiteMemberResponse(
    Guid UserId,
    string FullName,
    string Email,
    Guid AgencyId,
    string AgencyName,
    BusinessModel BusinessModel,
    [property: JsonConverter(typeof(OrganizationRoleJsonConverter))] OrganizationRole Role,
    bool IsActive,
    DateTimeOffset JoinedAt);
