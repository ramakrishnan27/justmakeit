using Microsoft.AspNetCore.Authorization;

namespace Platform.Auth.Services;

/// <summary>The caller's access token must carry <paramref name="Permission"/>.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
