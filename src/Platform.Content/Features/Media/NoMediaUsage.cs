using Platform.Content.Interfaces;

namespace Platform.Content.Features.Media;

/// <summary>There is no Media Library to tell, so a post's files are recorded nowhere but on the post.</summary>
public sealed class NoMediaUsage : IContentMediaUsage
{
    public Task ReplaceAsync(Guid agencyId, Guid userId, Guid postId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveAllAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken) => Task.CompletedTask;
}
