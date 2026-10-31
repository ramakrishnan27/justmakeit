namespace Platform.Auth.Models;

/// <summary>
/// Everything registration creates in one go. The ids are chosen up front so the connection can be
/// opened for the new tenant, which lets every row-level security policy pass without bypassing any.
/// </summary>
public sealed record NewAccount(
    Guid UserId,
    string Email,
    string PasswordHash,
    string FullName,
    Guid AgencyId,
    string AgencyName,
    Guid WorkspaceId,
    string WorkspaceName);
