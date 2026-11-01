using System.Text.Json.Serialization;
using Platform.Auth.Models;
using Platform.Helpers.Constants;

namespace Platform.Auth.DTOs;

public sealed record UserDto(Guid Id, string Email, string FullName);

/// <summary>The organization the token is for. It is called an agency in the code; <paramref name="BusinessModel"/> says what kind it is.</summary>
public sealed record AgencyDto(Guid Id, string Name, BusinessModel BusinessModel, AgencyPlan Plan);

public sealed record MembershipDto(
    Guid AgencyId,
    string AgencyName,
    BusinessModel BusinessModel,
    [property: JsonConverter(typeof(OrganizationRoleJsonConverter))] OrganizationRole Role);

/// <summary>The signed-in user, the organization the token is for, their role there, and every organization they belong to.</summary>
public sealed record MeResponse(
    UserDto User,
    AgencyDto Agency,
    [property: JsonConverter(typeof(OrganizationRoleJsonConverter))] OrganizationRole Role,
    IReadOnlyList<MembershipDto> Memberships);
