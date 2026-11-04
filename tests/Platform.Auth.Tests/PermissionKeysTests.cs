using System.Reflection;
using System.Text.RegularExpressions;
using Platform.Helpers.Constants;

namespace Platform.Auth.Tests;

public sealed partial class PermissionKeysTests
{
    // The same shape the permissions.key CHECK in 005_menus_permissions.sql accepts.
    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex KeyShape();

    private static string[] DeclaredKeys() =>
        typeof(PermissionKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, FieldType.Name: nameof(String) })
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

    [Fact]
    public void All_ListsEveryDeclaredKey_ExactlyOnce()
    {
        Assert.Equal(DeclaredKeys().Order(), PermissionKeys.All.Order());
        Assert.Equal(PermissionKeys.All.Count, PermissionKeys.All.Distinct().Count());
    }

    [Fact]
    public void ThereAreSevenKeys_SoAddingOneMeansSeedingItAndSayingWhoGetsIt() =>
        Assert.Equal(7, PermissionKeys.All.Count);

    [Fact]
    public void EveryKey_HasTheShapeTheDatabaseAccepts()
    {
        foreach (var key in PermissionKeys.All)
        {
            Assert.Matches(KeyShape(), key);
            Assert.InRange(key.Length, 1, 64);
        }
    }

    [Fact]
    public void OnlyTheDashboardPermission_IsProtected()
    {
        Assert.Equal([PermissionKeys.DashboardView], PermissionKeys.All.Where(PermissionKeys.IsProtected));
    }

    [Fact]
    public void EveryKey_BecomesAPolicyNameThatAnAttributeCanUse()
    {
        // A const expression, so a channel project can write it in [Authorize(Policy = ...)].
        const string policy = PolicyNames.PermissionPrefix + PermissionKeys.WorkspacesCreate;
        Assert.Equal("permission:workspaces.create", policy);
    }
}
