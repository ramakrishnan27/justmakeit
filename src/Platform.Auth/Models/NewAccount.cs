using Platform.Helpers.Constants;

namespace Platform.Auth.Models;

/// <summary>
/// Everything registration creates in one go. The ids are chosen up front so the connection can be
/// opened for the new tenant, which lets every row-level security policy pass without bypassing any.
/// <paramref name="Role"/> is the founder's role; the database refuses one the business model does not allow.
/// </summary>
public sealed record NewAccount(
    Guid UserId,
    string Email,
    string PasswordHash,
    string FullName,
    Guid AgencyId,
    string AgencyName,
    BusinessModel BusinessModel,
    OrganizationRole Role,
    Guid WorkspaceId,
    string WorkspaceName);
