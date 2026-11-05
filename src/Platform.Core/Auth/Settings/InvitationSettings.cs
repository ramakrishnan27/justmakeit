using Microsoft.Extensions.Options;

namespace Platform.Auth.Settings;

/// <summary>How long an invitation link works. Section <c>Auth</c>, key <c>InvitationDays</c>.</summary>
public sealed class InvitationSettings
{
    public const string SectionName = "Auth";

    public const int MinDays = 1;
    public const int MaxDays = 30;

    /// <summary>Days from the moment an invitation is made until its link stops working. Between 1 and 30.</summary>
    public int InvitationDays { get; set; } = 7;
}

public sealed class InvitationSettingsValidator : IValidateOptions<InvitationSettings>
{
    public ValidateOptionsResult Validate(string? name, InvitationSettings options) =>
        options.InvitationDays is < InvitationSettings.MinDays or > InvitationSettings.MaxDays
            ? ValidateOptionsResult.Fail(
                $"{InvitationSettings.SectionName}:InvitationDays must be between {InvitationSettings.MinDays} and {InvitationSettings.MaxDays}.")
            : ValidateOptionsResult.Success;
}
