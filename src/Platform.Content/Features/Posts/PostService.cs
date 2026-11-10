using Microsoft.Extensions.Logging;
using Platform.Content.Data;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Enums;
using Platform.Content.Features.Media;
using Platform.Content.Features.Scheduling;
using Platform.Content.Interfaces;
using Platform.Content.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Validation;

namespace Platform.Content.Features.Posts;

/// <summary>
/// The rules of posts. Each method re-checks the permission its endpoint names (so no other caller can skip it), validates, applies
/// the status rules of <see cref="ContentStatusMachine"/>, and leaves the writing to the repository. Titles, texts and captions are
/// never logged: only ids.
/// </summary>
public sealed class PostService(
    IContentPostRepository posts,
    IContentLookupRepository lookups,
    ICampaignLookup campaigns,
    IContentMediaLibrary media,
    IContentMediaUsage usage,
    ITenantContext tenant,
    TimeProvider time,
    ILogger<PostService> logger) : IPostService
{
    public async Task<PostPageResponse> ListAsync(PostListQuery query, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioView, "see content");
        query.Validate().ThrowIfAny();

        var filter = new PostFilter(
            query.WorkspaceId, query.Type, query.Status, query.Platform, query.CampaignId, query.From, query.To, query.Q?.Trim(), query.Page, query.PageSize);
        return PostMapper.ToPage(await posts.ListAsync(agencyId, userId, filter, cancellationToken), filter);
    }

    public async Task<PostResponse> GetAsync(Guid postId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioView, "see content");
        return await MediaHydration.ToResponseAsync(media, await LoadAsync(agencyId, userId, postId, cancellationToken), agencyId, userId, cancellationToken);
    }

    public async Task<PostResponse> CreateAsync(SavePostRequest request, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioCreate, "create content");

        var errors = new ValidationErrors();
        var post = PostBuilder.Build(request, null, agencyId, userId, time.GetUtcNow(), errors);
        errors.ThrowIfAny();

        if (!await lookups.WorkspaceExistsAsync(agencyId, userId, post.Post.WorkspaceId, cancellationToken))
        {
            throw new NotFoundException("That workspace was not found.", "workspace_not_found");
        }

        await EnsureReferencesAsync(post, null, agencyId, userId, cancellationToken);
        await posts.InsertAsync(userId, post, cancellationToken);
        await RecordUsageAsync(agencyId, userId, post, false, cancellationToken);

        logger.LogInformation("User {UserId} created post {PostId} in organization {AgencyId}", userId, post.Post.Id, agencyId);
        return await MediaHydration.ToResponseAsync(media, await LoadAsync(agencyId, userId, post.Post.Id, cancellationToken), agencyId, userId, cancellationToken);
    }

    public async Task<PostResponse> UpdateAsync(Guid postId, SavePostRequest request, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioCreate, "edit content");
        var existing = await LoadAsync(agencyId, userId, postId, cancellationToken);

        var status = DbEnum<ContentStatus>.Parse(existing.Post.Status);
        if (!ContentStatusMachine.IsEditable(status))
        {
            throw new ConflictException($"A post that is {existing.Post.Status} cannot be edited.", "post_not_editable");
        }

        var errors = new ValidationErrors();
        var edited = PostBuilder.Build(request, existing, agencyId, userId, time.GetUtcNow(), errors);
        errors.ThrowIfAny();

        // A scheduled post will go out as it is saved, so it must still be fit to go out.
        if (status == ContentStatus.Scheduled)
        {
            ScheduleReadiness.Check(edited).ThrowIfAny();
        }

        await EnsureReferencesAsync(edited, existing, agencyId, userId, cancellationToken);
        var outcome = await posts.UpdateAsync(userId, edited, request.ExpectedUpdatedAt!.Value, cancellationToken);
        switch (outcome)
        {
            case SaveOutcome.Changed:
                throw new ConflictException("This post was changed after you opened it. Reload it to see the latest version.", "content_changed");
            case SaveOutcome.Busy:
                throw new ConflictException("This post is being published, so it cannot be edited now.", "post_publishing");
        }

        await RecordUsageAsync(agencyId, userId, edited, existing.Media.Count > 0, cancellationToken);

        logger.LogInformation("User {UserId} edited post {PostId} in organization {AgencyId}", userId, postId, agencyId);
        return await MediaHydration.ToResponseAsync(media, await LoadAsync(agencyId, userId, postId, cancellationToken), agencyId, userId, cancellationToken);
    }

    public async Task<PostResponse> DuplicateAsync(Guid postId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioCreate, "create content");
        var source = await LoadAsync(agencyId, userId, postId, cancellationToken);

        var copy = PostCopier.Copy(source, userId, time.GetUtcNow());
        await posts.InsertAsync(userId, copy, cancellationToken);
        await RecordUsageAsync(agencyId, userId, copy, false, cancellationToken);

        logger.LogInformation("User {UserId} duplicated post {PostId} as {CopyId}", userId, postId, copy.Post.Id);
        return await MediaHydration.ToResponseAsync(media, await LoadAsync(agencyId, userId, copy.Post.Id, cancellationToken), agencyId, userId, cancellationToken);
    }

    public async Task DeleteAsync(Guid postId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioView, "see content");
        var existing = await LoadAsync(agencyId, userId, postId, cancellationToken);

        var status = DbEnum<ContentStatus>.Parse(existing.Post.Status);
        if (!ContentStatusMachine.IsDeletable(status))
        {
            throw new ConflictException($"A post that is {existing.Post.Status} cannot be deleted.", "post_not_deletable");
        }

        // A draft is the writer's to delete; anything that has been through publishing is the publisher's.
        if (status == ContentStatus.Draft)
        {
            ContentAccess.Require(tenant, PermissionKeys.ContentStudioCreate, "delete drafts");
        }
        else
        {
            ContentAccess.Require(tenant, PermissionKeys.ContentStudioPublish, "delete this content");
        }

        var deletable = new[] { ContentStatus.Draft, ContentStatus.Failed, ContentStatus.Archived }.Select(s => s.ToWire()).ToList();
        if (!await posts.DeleteAsync(agencyId, userId, postId, deletable, cancellationToken))
        {
            throw new ConflictException("This post changed while you were deleting it. Reload it and try again.", "invalid_transition");
        }

        await ForgetUsageAsync(agencyId, userId, postId, cancellationToken);

        logger.LogInformation("User {UserId} deleted post {PostId} in organization {AgencyId}", userId, postId, agencyId);
    }

    public async Task<PostResponse> ArchiveAsync(Guid postId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioPublish, "archive content");
        var existing = await LoadAsync(agencyId, userId, postId, cancellationToken);

        var status = DbEnum<ContentStatus>.Parse(existing.Post.Status);
        if (!ContentStatusMachine.CanMove(status, ContentStatus.Archived))
        {
            throw new ConflictException($"A post that is {existing.Post.Status} cannot be archived.", "invalid_transition");
        }

        var from = ContentStatusMachine.Sources(ContentStatus.Archived).Select(s => s.ToWire()).ToList();
        if (!await posts.ArchiveAsync(agencyId, userId, postId, from, time.GetUtcNow(), cancellationToken))
        {
            throw new ConflictException("This post changed while you were archiving it. Reload it and try again.", "invalid_transition");
        }

        logger.LogInformation("User {UserId} archived post {PostId}", userId, postId);
        return await MediaHydration.ToResponseAsync(media, await LoadAsync(agencyId, userId, postId, cancellationToken), agencyId, userId, cancellationToken);
    }

    public async Task<PostResponse> RestoreAsync(Guid postId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioPublish, "restore content");
        var existing = await LoadAsync(agencyId, userId, postId, cancellationToken);

        if (DbEnum<ContentStatus>.Parse(existing.Post.Status) != ContentStatus.Archived)
        {
            throw new ConflictException("Only an archived post can be restored.", "invalid_transition");
        }

        if (await posts.RestoreAsync(agencyId, userId, postId, cancellationToken) is null)
        {
            throw new ConflictException("This post changed while you were restoring it. Reload it and try again.", "invalid_transition");
        }

        logger.LogInformation("User {UserId} restored post {PostId}", userId, postId);
        return await MediaHydration.ToResponseAsync(media, await LoadAsync(agencyId, userId, postId, cancellationToken), agencyId, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<RevisionResponse>> ListRevisionsAsync(Guid postId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioView, "see content");
        if (!await posts.ExistsAsync(agencyId, userId, postId, cancellationToken))
        {
            throw new NotFoundException("That post was not found.", "post_not_found");
        }

        var revisions = await posts.ListRevisionsAsync(agencyId, userId, postId, cancellationToken);
        return revisions.Select(r => new RevisionResponse(r.RevisionNumber, r.Title, r.BodyLength, r.ChangedBy, r.CreatedAt)).ToList();
    }

    private async Task<ContentPostAggregate> LoadAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken) =>
        await posts.GetAsync(agencyId, userId, postId, cancellationToken)
        ?? throw new NotFoundException("That post was not found.", "post_not_found");

    private async Task EnsureReferencesAsync(
        ContentPostAggregate post, ContentPostAggregate? existing, Guid agencyId, Guid userId, CancellationToken cancellationToken)
    {
        await PlatformAccess.EnsureCampaignAsync(campaigns, agencyId, userId, post.Post.CampaignId, cancellationToken);
        await PlatformAccess.EnsureEnabledAsync(lookups, agencyId, userId, post.Platforms, cancellationToken);
        await MediaAccess.EnsureAsync(media, tenant, post, existing, agencyId, userId, cancellationToken);
    }

    /// <summary>
    /// Tells the library which files the post uses. It runs after the post is saved and cannot be part of that transaction (the library's
    /// tables are not Content's to touch), so a failure is logged, with ids only, and does not fail what the person did: the post's own
    /// rows already protect the files from deletion, and the next save sends the whole list again.
    /// </summary>
    private async Task RecordUsageAsync(
        Guid agencyId, Guid userId, ContentPostAggregate post, bool hadFiles, CancellationToken cancellationToken)
    {
        // A post that has no files and never had any has nothing to tell the library, and most posts are like that.
        if (post.Media.Count == 0 && !hadFiles)
        {
            return;
        }

        try
        {
            await usage.ReplaceAsync(agencyId, userId, post.Post.Id, [.. post.Media.Select(m => m.MediaAssetId)], cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The Media Library was not told which files post {PostId} of organization {AgencyId} uses", post.Post.Id, agencyId);
        }
    }

    private async Task ForgetUsageAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken)
    {
        try
        {
            await usage.RemoveAllAsync(agencyId, userId, postId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The Media Library was not told that post {PostId} of organization {AgencyId} is gone", postId, agencyId);
        }
    }
}
