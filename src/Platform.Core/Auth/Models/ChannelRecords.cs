namespace Platform.Auth.Models;

/// <summary>One row of the channel list (channel_rows). The creator is a name, or an address when there is no name; null for a built-in channel.</summary>
public sealed class SiteChannelRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>The built-in icon the web client draws, or null when the channel has an uploaded image.</summary>
    public string? IconKey { get; set; }

    public bool HasImage { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>How many organizations have the channel enabled.</summary>
    public long OrganizationCount { get; set; }
}

/// <summary>A channel as seen from one organization: whether it is enabled there.</summary>
public sealed class OrganizationChannelRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconKey { get; set; }
    public bool HasImage { get; set; }
    public bool IsEnabled { get; set; }
}

/// <summary>The uploaded image of a channel.</summary>
public sealed class ChannelIconRecord
{
    public string ContentType { get; set; } = string.Empty;
    public byte[] Image { get; set; } = [];
}

/// <summary>What the database needs to create a channel: an icon key or an image with its type, not both.</summary>
public sealed record NewChannel(string Name, string Description, string? IconKey, byte[]? Image, string? ContentType);
