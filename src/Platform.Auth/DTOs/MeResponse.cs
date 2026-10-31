using Platform.Auth.Models;
using Platform.Helpers.Constants;

namespace Platform.Auth.DTOs;

public sealed record UserDto(Guid Id, string Email, string FullName);

public sealed record AgencyDto(Guid Id, string Name, AgencyPlan Plan);

public sealed record MembershipDto(Guid AgencyId, string AgencyName, AgencyRole Role);

/// <summary>The signed-in user, the agency the token is for, their role there, and every agency they belong to.</summary>
public sealed record MeResponse(UserDto User, AgencyDto Agency, AgencyRole Role, IReadOnlyList<MembershipDto> Memberships);
