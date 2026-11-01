using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Utilities;
using Platform.Helpers.Validation;
using Platform.Infrastructure.Data;

namespace Platform.Auth.DTOs;

/// <summary>Field checks shared by the request DTOs, so every request words and limits them the same way.</summary>
internal static class FieldRules
{
    public const int MaxNameLength = 200;
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 128;

    public static void Email(ValidationErrors errors, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("email", "Email is required.");
        }
        else if (!EmailAddressHelper.IsValid(value))
        {
            errors.Add("email", "Enter a valid email address.");
        }
    }

    public static void NewPassword(ValidationErrors errors, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            errors.Add("password", "Password is required.");
        }
        else if (value.Length < MinPasswordLength)
        {
            errors.Add("password", $"Password must be at least {MinPasswordLength} characters.");
        }
        else if (value.Length > MaxPasswordLength)
        {
            errors.Add("password", $"Password must be at most {MaxPasswordLength} characters.");
        }
    }

    /// <summary>Login checks only that a usable password was sent, not the rules for choosing one.</summary>
    public static void ExistingPassword(ValidationErrors errors, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            errors.Add("password", "Password is required.");
        }
        else if (value.Length > MaxPasswordLength)
        {
            errors.Add("password", $"Password must be at most {MaxPasswordLength} characters.");
        }
    }

    public static void RequiredName(ValidationErrors errors, string field, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, $"{label} is required.");
        }
        else if (value.Trim().Length > MaxNameLength)
        {
            errors.Add(field, $"{label} must be at most {MaxNameLength} characters.");
        }
    }

    public static void RequiredBusinessModel(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Business model is required.");
        }
        else if (!BusinessModelRules.TryParse(value, out _))
        {
            var names = string.Join(", ", Enum.GetValues<BusinessModel>().Select(model => model.ToDb()));
            errors.Add(field, $"Business model must be one of: {names}.");
        }
    }

    public static void OptionalName(ValidationErrors errors, string field, string label, string? value)
    {
        if (value is not null && value.Trim().Length > MaxNameLength)
        {
            errors.Add(field, $"{label} must be at most {MaxNameLength} characters.");
        }
    }
}
