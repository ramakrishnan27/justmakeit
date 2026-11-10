namespace Platform.Content.Settings;

/// <summary>The <c>Content</c> section of the configuration.</summary>
public sealed class ContentOptions
{
    public const string SectionName = "Content";

    public PublishingOptions Publishing { get; set; } = new();
}

public sealed class PublishingOptions
{
    /// <summary>Whether this process delivers what is due. Turn it off for a second instance that should only serve requests.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>
    /// Whether a development fake publisher stands in for every platform that has no real publisher. Off by default and on in
    /// Development: in production a post to a platform with no publisher fails with a clear message instead of pretending.
    /// </summary>
    public bool UseFakePublisher { get; set; }

    /// <summary>How often the worker looks for what is due.</summary>
    public int PollSeconds { get; set; } = 15;

    /// <summary>The most targets claimed in one look.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>How many times a target is tried before it is failed for good.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>A target left publishing by a process that died is claimed again after this long.</summary>
    public int StaleMinutes { get; set; } = 10;

    /// <summary>Wait this long after start-up before the first look, so the application is ready.</summary>
    public int StartupDelaySeconds { get; set; } = 5;
}
