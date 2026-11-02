using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>Adds a built-in page to a menu. A missing <c>isVisible</c> means visible.</summary>
public sealed record CreateMenuItemRequest(string? PageKey, string? Label, string? Icon, bool? IsVisible)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.PageKey(errors, "pageKey", PageKey);
        FieldRules.MenuLabel(errors, "label", Label);
        FieldRules.MenuIcon(errors, "icon", Icon);
        return errors;
    }
}

/// <summary>Renames an item, changes its icon, and shows or hides it. All three are sent every time.</summary>
public sealed record UpdateMenuItemRequest(string? Label, string? Icon, bool? IsVisible)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.MenuLabel(errors, "label", Label);
        FieldRules.MenuIcon(errors, "icon", Icon);
        if (IsVisible is null)
        {
            errors.Add("isVisible", "Visibility is required.");
        }

        return errors;
    }
}

/// <summary>The new order: every item id of the menu, once each, first to last.</summary>
public sealed record ReorderMenuRequest(IReadOnlyList<Guid>? ItemIds)
{
    public const int MaxItems = 200;

    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        if (ItemIds is null)
        {
            errors.Add("itemIds", "The list of items is required.");
        }
        else if (ItemIds.Count > MaxItems)
        {
            errors.Add("itemIds", $"A menu has at most {MaxItems} items.");
        }
        else if (ItemIds.Any(id => id == Guid.Empty))
        {
            errors.Add("itemIds", "Every item needs an id.");
        }
        else if (ItemIds.Distinct().Count() != ItemIds.Count)
        {
            errors.Add("itemIds", "An item can be listed only once.");
        }

        return errors;
    }
}
