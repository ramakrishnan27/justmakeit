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

    public static void RequiredRole(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Role is required.");
        }
        else if (!RolePermissions.TryParse(value, out _))
        {
            var names = string.Join(", ", Enum.GetValues<OrganizationRole>().Select(role => role.ToDb()));
            errors.Add(field, $"Role must be one of: {names}.");
        }
    }

    /// <summary>
    /// The secret of an invitation link (a 32-byte token in unpadded base64url is 43 characters). Only the shape is
    /// checked here; whether it opens an invitation is the database's answer and looks the same for every miss.
    /// </summary>
    public static void InvitationToken(ValidationErrors errors, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("token", "The invitation link is incomplete.");
        }
        else if (value.Length > MaxInvitationTokenLength)
        {
            errors.Add("token", "The invitation link is not valid.");
        }
    }

    public const int MaxInvitationTokenLength = 200;

    /// <summary>The words on a menu item. Trimmed before it is stored; no line breaks or other control characters.</summary>
    public static void MenuLabel(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Label is required.");
        }
        else if (value.Trim().Length > MaxMenuLabelLength)
        {
            errors.Add(field, $"Label must be at most {MaxMenuLabelLength} characters.");
        }
        else if (value.Any(char.IsControl))
        {
            errors.Add(field, "Label cannot contain line breaks or other control characters.");
        }
    }

    public static void MenuIcon(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Icon is required.");
        }
        else if (!MenuIcons.IsKnown(value))
        {
            errors.Add(field, $"Icon must be one of: {string.Join(", ", MenuIcons.All)}.");
        }
    }

    /// <summary>The key of a built-in page: lower-case words with hyphens, at most 40 characters. Whether the page exists is the database's answer.</summary>
    public static void PageKey(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Page is required.");
        }
        else if (value.Length > 40 || !char.IsAsciiLetterLower(value[0]) || !value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            errors.Add(field, "Page is not a valid page key.");
        }
    }

    public const int MaxMenuLabelLength = 60;
}
