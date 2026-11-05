using Platform.Helpers.Constants;

namespace Platform.Auth.Models;

/// <summary>
/// Everything registration creates in one go. The ids are chosen up front so the connection can be
/// opened for the new tenant, which lets every row-level security policy pass without bypassing any.
/// <paramref name="Role"/> is the founder's role; the database refuses one the business model does not allow.
/// A person who signs up with Google has no <paramref name="PasswordHash"/> and brings an
/// <paramref name="Identity"/> instead; the database requires one or the other.
/// </summary>
public sealed record NewAccount(
    Guid UserId,
    string Email,
    string? PasswordHash,
    string FullName,
    Guid AgencyId,
    string AgencyName,
    BusinessModel BusinessModel,
    OrganizationRole Role,
    Guid WorkspaceId,
    string WorkspaceName,
    ExternalIdentity? Identity = null);

/// <summary>An outside sign-in (<see cref="ExternalProviders"/>) to attach to a new user.</summary>
public sealed record ExternalIdentity(string Provider, string Subject, string Email);
