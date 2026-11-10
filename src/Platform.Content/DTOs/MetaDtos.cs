using System.Text.Json;
using Platform.Helpers.Validation;

namespace Platform.Content.DTOs;

public sealed record CreateTagRequest(Guid? WorkspaceId, string? Name, string? Color);

/// <summary>A hashtag the workspace has used, and how often, for the suggestions under the editor.</summary>
public sealed record HashtagSuggestionResponse(string Hashtag, int Uses);

/// <summary>The suggestions' query: <c>?workspaceId=&amp;q=gro&amp;limit=8</c>.</summary>
public sealed record HashtagSuggestionQuery(Guid? WorkspaceId = null, string? Q = null, int Limit = HashtagSuggestionQuery.DefaultLimit)
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 30;

    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        if (Limit is < 1 or > MaxLimit)
        {
            errors.Add("limit", $"Limit must be between 1 and {MaxLimit}.");
        }

        if (Q is { Length: > 100 })
        {
            errors.Add("q", "Search must be at most 100 characters.");
        }

        return errors;
    }
}

/// <summary>What is real in this installation, so the page can switch a panel off instead of letting it fail.</summary>
public sealed record CapabilitiesResponse(bool Media, bool Ai, bool Campaigns, string Publishing);

/// <summary>A platform's limits and whether the organization may use it. The limits are for showing a counter and for checking before scheduling.</summary>
public sealed record PlatformRuleResponse(string Platform, string Name, bool Enabled, int CaptionLimit, int? TitleLimit, int? MaxHashtags);

/// <summary>A template as the Create page uses it: everything needed to fill the editor.</summary>
public sealed record TemplateResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Description,
    string ContentType,
    string Title,
    JsonElement? BodyDoc,
    string BodyText,
    IReadOnlyList<string> Hashtags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class SaveTemplateRequest
{
    public Guid? WorkspaceId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? ContentType { get; init; }
    public string? Title { get; init; }
    public JsonElement? BodyDoc { get; init; }
    public string[]? Hashtags { get; init; }
}

/// <summary>The templates' query: <c>?workspaceId=&amp;type=social_post</c>.</summary>
public sealed record TemplateListQuery(Guid? WorkspaceId = null, string? Type = null);
