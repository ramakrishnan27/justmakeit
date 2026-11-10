using System.Text.RegularExpressions;
using Platform.Helpers.Media;
using Platform.Media.Interfaces;

namespace Platform.Media.Services;

/// <summary>
/// The Media Library's side of the usage contract in Core: a feature says where it uses a file, the library keeps the record. It
/// checks what the table's own CHECKs check (a lower-case name of letters, digits and underscores) so a mistake in a caller is an
/// <see cref="ArgumentException"/> at once and not a database error later. The caller has already checked the person's permission.
/// </summary>
public sealed partial class MediaAssetUsageRegistry(IMediaUsageRepository usages) : IMediaAssetUsageRegistry
{
    /// <summary>The most files one thing can use; far above what any feature allows, so a runaway list cannot reach the database.</summary>
    public const int MaxAssetsPerEntity = 500;

    [GeneratedRegex("^[a-z][a-z0-9_]{0,49}$")]
    private static partial Regex NamePattern();

    public Task ReplaceAsync(
        Guid agencyId,
        Guid userId,
        string moduleName,
        string entityType,
        Guid entityId,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken)
    {
        Check(agencyId, userId, moduleName, entityType, entityId);
        ArgumentNullException.ThrowIfNull(assetIds);
        if (assetIds.Count > MaxAssetsPerEntity)
        {
            throw new ArgumentException($"A thing can use at most {MaxAssetsPerEntity} files.", nameof(assetIds));
        }

        return usages.ReplaceAsync(agencyId, userId, moduleName, entityType, entityId, [.. assetIds.Distinct()], cancellationToken);
    }

    public Task RemoveAllAsync(
        Guid agencyId, Guid userId, string moduleName, string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        Check(agencyId, userId, moduleName, entityType, entityId);
        return usages.ReplaceAsync(agencyId, userId, moduleName, entityType, entityId, [], cancellationToken);
    }

    private static void Check(Guid agencyId, Guid userId, string moduleName, string entityType, Guid entityId)
    {
        if (agencyId == Guid.Empty || userId == Guid.Empty || entityId == Guid.Empty)
        {
            throw new ArgumentException("An organization, a user and the thing that uses the file are required.");
        }

        if (!IsValidName(moduleName))
        {
            throw new ArgumentException("The module name is lower case letters, digits and underscores, starting with a letter.", nameof(moduleName));
        }

        if (!IsValidName(entityType))
        {
            throw new ArgumentException("The kind of thing is lower case letters, digits and underscores, starting with a letter.", nameof(entityType));
        }
    }

    /// <summary>Whether a module or entity name is one the table accepts.</summary>
    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);
}
