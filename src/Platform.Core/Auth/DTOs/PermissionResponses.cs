namespace Platform.Auth.DTOs;

/// <summary>A kind of organization and a role in it. Both are the stored lower-case names, for example <c>agency</c> and <c>clientmanager</c>.</summary>
public sealed record SlotResponse(string BusinessModel, string Role);

/// <summary>Whether one slot holds a permission.</summary>
public sealed record GrantResponse(string BusinessModel, string Role, bool Granted);

public sealed record PermissionResponse(
    string Key, string Title, string Description, bool IsProtected, IReadOnlyList<GrantResponse> Grants);

/// <summary>Every permission and who holds it: the grid the Site admin edits.</summary>
public sealed record PermissionMatrixResponse(IReadOnlyList<SlotResponse> Slots, IReadOnlyList<PermissionResponse> Permissions);

/// <summary>The grant a change left behind.</summary>
public sealed record PermissionGrantResponse(string PermissionKey, string BusinessModel, string Role, bool Granted);
