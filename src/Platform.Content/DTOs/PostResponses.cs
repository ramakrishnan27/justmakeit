using System.Text.Json;

namespace Platform.Content.DTOs;

/// <summary>A post as the Edit page needs it: everything, including the document and every child.</summary>
public sealed record PostResponse(
    Guid Id,
    Guid WorkspaceId,
    Guid? CampaignId,
    string ContentType,
    string Title,
    JsonElement? BodyDoc,
    string BodyText,
    string Excerpt,
    string Status,
    string Visibility,
    string LanguageCode,
    string Timezone,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ArchivedAt,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoSlug,
    IReadOnlyList<string> SeoKeywords,
    IReadOnlyList<string> Hashtags,
    IReadOnlyList<TagResponse> Tags,
    IReadOnlyList<PostPlatformResponse> Platforms,
    PollResponse? Poll,
    IReadOnlyList<CtaResponse> Ctas,
    IReadOnlyList<MentionResponse> Mentions,
    LocationResponse? Location,
    IReadOnlyList<PostMediaResponse> Media,
    JsonElement Settings,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TagResponse(Guid Id, string Name, string Color);

/// <summary>One platform a post goes to and how it is doing there. <c>LastError</c> is words for a person, never a token or a raw response.</summary>
public sealed record PostPlatformResponse(
    string Platform,
    string? Caption,
    string? Title,
    JsonElement Settings,
    string PublishStatus,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? PublishedAt,
    string? ExternalPostUrl,
    string? LastError,
    int RetryCount);

public sealed record PollResponse(string Question, DateTimeOffset? EndsAt, IReadOnlyList<string> Options);

public sealed record CtaResponse(string ButtonText, string TargetUrl);

public sealed record MentionResponse(string Platform, string Name);

public sealed record LocationResponse(string Name, decimal? Latitude, decimal? Longitude);

/// <summary>
/// A file on a post: where it sits and what it is for, with what the library says about it so the page can show it without a second
/// call. The bytes are at <c>/v1/media/assets/{assetId}/file</c> for someone who may view the library.
/// </summary>
public sealed record PostMediaResponse(
    Guid AssetId,
    string Role,
    int DisplayOrder,
    string Caption,
    string FileName,
    string MediaType,
    string MimeType,
    long SizeBytes,
    int? Width,
    int? Height);

/// <summary>One row of the list: enough for the table or the card, nothing that needs a second query on the page.</summary>
public sealed record PostListItemResponse(
    Guid Id,
    Guid WorkspaceId,
    string ContentType,
    string Title,
    string Excerpt,
    string Status,
    string Visibility,
    IReadOnlyList<PostListPlatform> Platforms,
    IReadOnlyList<TagResponse> Tags,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A platform a listed post goes to, with where it is there: the icon is drawn from the first, the status from the second.</summary>
public sealed record PostListPlatform(string Platform, string PublishStatus);

/// <summary>
/// A page of the list. <c>TypeCounts</c> is the number of posts per content type under every filter except the type, plus
/// <c>All</c>, so the tabs keep their numbers while one of them is open.
/// </summary>
public sealed record PostPageResponse(
    IReadOnlyList<PostListItemResponse> Items,
    int Page,
    int PageSize,
    long TotalCount,
    int TotalPages,
    IReadOnlyDictionary<string, long> TypeCounts);

/// <summary>One saved version of a post's text. Who saved it and when; the text itself is not sent.</summary>
public sealed record RevisionResponse(int RevisionNumber, string Title, int BodyLength, Guid? ChangedBy, DateTimeOffset CreatedAt);
