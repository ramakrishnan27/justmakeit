using System.Diagnostics.CodeAnalysis;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Services;

/// <summary>
/// What each role may do, in one place. The authorization policies are built from it and services
/// re-check against it, so the two can never disagree.
/// </summary>
public static class RolePermissions
{
    /// <summary>Owners, admins and managers change data; viewers only read.</summary>
    public static bool CanWrite(this AgencyRole role) => role != AgencyRole.Viewer;

    /// <summary>Owners and admins manage the agency: its members and its workspaces.</summary>
    public static bool CanManage(this AgencyRole role) => role is AgencyRole.Owner or AgencyRole.Admin;

    public static bool CanRead(this AgencyRole role) => Enum.IsDefined(role);

    /// <summary>
    /// Reads a role from its stored lower-case name, such as the token's role claim. Strict on purpose:
    /// anything but the exact name, including a number, is not a role.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out AgencyRole role)
    {
        foreach (var candidate in Enum.GetValues<AgencyRole>())
        {
            if (value == candidate.ToDb())
            {
                role = candidate;
                return true;
            }
        }

        role = default;
        return false;
    }

    /// <summary>The stored names of every role that passes <paramref name="allowed"/>.</summary>
    public static string[] NamesWhere(Func<AgencyRole, bool> allowed) =>
        Enum.GetValues<AgencyRole>().Where(allowed).Select(role => role.ToDb()).ToArray();
}
