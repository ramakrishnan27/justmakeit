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
    /// <summary>
    /// Everyone who is a member reads. A client manager reads but does not change data: their work
    /// (relationships, feedback, approvals) gets its own rule when those features exist.
    /// </summary>
    public static bool CanRead(this OrganizationRole role) => Enum.IsDefined(role);

    /// <summary>Organization admins, marketing managers and content writers change data.</summary>
    public static bool CanWrite(this OrganizationRole role) =>
        role is OrganizationRole.OrganizationAdmin or OrganizationRole.MarketingManager or OrganizationRole.ContentWriter;

    /// <summary>Only organization admins manage the organization: its members and its workspaces.</summary>
    public static bool CanManage(this OrganizationRole role) => role is OrganizationRole.OrganizationAdmin;

    /// <summary>
    /// Reads a role from its stored lower-case name, such as the token's role claim. Strict on purpose:
    /// anything but the exact name, including a number, is not a role.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out OrganizationRole role)
    {
        foreach (var candidate in Enum.GetValues<OrganizationRole>())
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
    public static string[] NamesWhere(Func<OrganizationRole, bool> allowed) =>
        Enum.GetValues<OrganizationRole>().Where(allowed).Select(role => role.ToDb()).ToArray();
}
