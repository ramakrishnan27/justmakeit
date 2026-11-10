namespace Platform.Auth.DTOs;

/// <summary>A platform (channel) as a member's Platform page shows it: no creator, no counts, no image bytes.</summary>
public sealed record PlatformResponse(
    Guid Id,
    string Name,
    string Description,
    string? IconKey,
    bool HasIconImage,
    bool IsEnabled);
