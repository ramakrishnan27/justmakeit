namespace Platform.Helpers.Media;

/// <summary>
/// How a feature tells the Media Library where it uses a file, without referencing the Media project or touching its tables. The
/// library shows the count ("Used in 1 post") and does not offer to delete a file that something uses; the database already refuses
/// the delete, this is what lets the page say so. It lives in Core for the reason <see cref="IMediaAssetCatalog"/> does.
/// <para>
/// A usage is (module, kind of thing, which one): Content Studio writes <c>content_studio</c>, <c>post</c> and the post's id. The
/// feature owns its usages: it replaces the set whenever the thing changes and removes them when the thing is deleted. Neither call
/// shares a transaction with the feature's own save, so a caller treats a failure as something to log and to heal on its next save,
/// never as a reason to fail what the person did.
/// </para>
/// </summary>
public interface IMediaAssetUsageRegistry
{
    /// <summary>
    /// Makes the files in <paramref name="assetIds"/> the files this thing uses: a usage that is missing is added, one for a file that
    /// is no longer in the set is removed. An empty set removes them all. An id that is not a file of the organization is ignored (a
    /// usage needs a file). Runs for the given organization and user with row-level security on. Safe to repeat.
    /// </summary>
    Task ReplaceAsync(
        Guid agencyId,
        Guid userId,
        string moduleName,
        string entityType,
        Guid entityId,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken);

    /// <summary>Removes every usage of this thing. Safe to repeat.</summary>
    Task RemoveAllAsync(
        Guid agencyId, Guid userId, string moduleName, string entityType, Guid entityId, CancellationToken cancellationToken);
}
