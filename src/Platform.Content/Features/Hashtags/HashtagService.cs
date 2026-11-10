using Microsoft.Extensions.Logging;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Interfaces;
using Platform.Content.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Validation;

namespace Platform.Content.Features.Hashtags;

/// <summary>Hashtag suggestions from what the organization has already used, and the tags of a workspace.</summary>
public sealed class HashtagService(
    IHashtagRepository hashtags,
    IContentLookupRepository lookups,
    ITenantContext tenant,
    ILogger<HashtagService> logger) : IHashtagService
{
    public async Task<IReadOnlyList<HashtagSuggestionResponse>> SuggestAsync(HashtagSuggestionQuery query, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioView, "see content");
        query.Validate().ThrowIfAny();

        // What was typed after the #, or nothing: an empty prefix means the most used.
        var prefix = (query.Q ?? string.Empty).Trim().TrimStart('#').Trim();
        var rows = await hashtags.SuggestAsync(agencyId, userId, query.WorkspaceId, prefix.Length == 0 ? null : prefix, query.Limit, cancellationToken);
        return rows.Select(row => new HashtagSuggestionResponse(row.Hashtag, row.Uses)).ToList();
    }

    public async Task<IReadOnlyList<TagResponse>> ListTagsAsync(Guid? workspaceId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioView, "see content");
        var tags = await hashtags.ListTagsAsync(agencyId, userId, workspaceId, cancellationToken);
        return tags.Select(tag => new TagResponse(tag.Id, tag.Name, tag.Color)).ToList();
    }

    public async Task<TagResponse> CreateTagAsync(CreateTagRequest request, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioCreate, "create tags");

        var errors = new ValidationErrors();
        if (request.WorkspaceId is null || request.WorkspaceId == Guid.Empty)
        {
            errors.Add("workspaceId", "Choose a workspace.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 50 || name.Any(char.IsControl))
        {
            errors.Add("name", "A tag name is 1 to 50 characters on one line.");
        }

        if (request.Color is not null && !TagColors.IsValid(request.Color))
        {
            errors.Add("color", "Colour must be # and six hex digits, such as #1F7A55.");
        }

        errors.ThrowIfAny();

        if (!await lookups.WorkspaceExistsAsync(agencyId, userId, request.WorkspaceId!.Value, cancellationToken))
        {
            throw new NotFoundException("That workspace was not found.", "workspace_not_found");
        }

        var tag = new ContentTag
        {
            Id = Guid.NewGuid(),
            AgencyId = agencyId,
            WorkspaceId = request.WorkspaceId.Value,
            Name = name,
            Color = request.Color ?? TagColors.For(name)
        };
        if (!await hashtags.InsertTagAsync(userId, tag, cancellationToken))
        {
            throw new ConflictException("This workspace already has a tag with that name.", "tag_exists");
        }

        logger.LogInformation("User {UserId} created tag {TagId} in workspace {WorkspaceId}", userId, tag.Id, tag.WorkspaceId);
        return new TagResponse(tag.Id, tag.Name, tag.Color);
    }
}
