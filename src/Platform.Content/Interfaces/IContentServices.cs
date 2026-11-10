using Platform.Content.DTOs;
using Platform.Helpers.Publishing;

namespace Platform.Content.Interfaces;

/// <summary>Posts: the list, one post, saving, duplicating, archiving, deleting, and the history of its text.</summary>
public interface IPostService
{
    Task<PostPageResponse> ListAsync(PostListQuery query, CancellationToken cancellationToken);

    Task<PostResponse> GetAsync(Guid postId, CancellationToken cancellationToken);

    Task<PostResponse> CreateAsync(SavePostRequest request, CancellationToken cancellationToken);

    Task<PostResponse> UpdateAsync(Guid postId, SavePostRequest request, CancellationToken cancellationToken);

    Task<PostResponse> DuplicateAsync(Guid postId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid postId, CancellationToken cancellationToken);

    Task<PostResponse> ArchiveAsync(Guid postId, CancellationToken cancellationToken);

    Task<PostResponse> RestoreAsync(Guid postId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RevisionResponse>> ListRevisionsAsync(Guid postId, CancellationToken cancellationToken);
}

/// <summary>Setting and taking back the schedule of a post, and publishing it now.</summary>
public interface ISchedulingService
{
    Task<PostResponse> ScheduleAsync(Guid postId, ScheduleRequest request, CancellationToken cancellationToken);

    Task<PostResponse> UnscheduleAsync(Guid postId, CancellationToken cancellationToken);

    Task<PostResponse> PublishNowAsync(Guid postId, CancellationToken cancellationToken);
}

/// <summary>Delivering what is due. Called by the background worker; not tied to a request.</summary>
public interface IPublishingService
{
    /// <summary>Claims what is due and delivers each target through its platform's publisher. The number handled.</summary>
    Task<int> ProcessDueAsync(CancellationToken cancellationToken);
}

public interface IHashtagService
{
    Task<IReadOnlyList<HashtagSuggestionResponse>> SuggestAsync(HashtagSuggestionQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<TagResponse>> ListTagsAsync(Guid? workspaceId, CancellationToken cancellationToken);

    Task<TagResponse> CreateTagAsync(CreateTagRequest request, CancellationToken cancellationToken);
}

public interface ITemplateService
{
    Task<IReadOnlyList<TemplateResponse>> ListAsync(TemplateListQuery query, CancellationToken cancellationToken);

    Task<TemplateResponse> CreateAsync(SaveTemplateRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid templateId, CancellationToken cancellationToken);
}

public interface ICapabilitiesService
{
    CapabilitiesResponse GetCapabilities();

    Task<IReadOnlyList<PlatformRuleResponse>> GetPlatformRulesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The publishers that exist, by platform. Real ones come from the channel projects through <see cref="ISocialPublisher"/>; where
/// <c>Content:Publishing:UseFakePublisher</c> is on, a development fake stands in for every platform that has no real one.
/// </summary>
public interface IPublisherCatalog
{
    ISocialPublisher? Find(string platform);

    /// <summary><c>none</c> (nothing can publish), <c>fake</c> (the development fake is on) or <c>live</c> (a real publisher exists).</summary>
    string Mode { get; }
}
