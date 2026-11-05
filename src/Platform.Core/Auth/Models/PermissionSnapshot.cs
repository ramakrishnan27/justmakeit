using Platform.Helpers.Constants;

namespace Platform.Auth.Models;

/// <summary>
/// Every permission grant and the version they were read under, in one consistent piece. Small enough
/// (eight business model and role pairs by a handful of keys) to be held whole in memory.
/// </summary>
public sealed record PermissionSnapshot(
    long Version,
    IReadOnlyDictionary<(BusinessModel BusinessModel, OrganizationRole Role), IReadOnlyList<string>> Granted);
