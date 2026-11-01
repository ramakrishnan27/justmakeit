using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface IAccountRepository
{
    /// <summary>
    /// Finds a user by normalized email through the narrow auth_find_user_by_email() function: login
    /// needs the row before any tenant is known, and row-level security hides users from everybody else.
    /// </summary>
    Task<UserLoginRecord?> FindLoginByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>
    /// The user an outside sign-in (for example Google's id for the person) belongs to, or null. Like the email
    /// lookup it needs no tenant: it goes through auth_find_user_by_identity().
    /// </summary>
    Task<Guid?> FindUserIdByIdentityAsync(string provider, string subject, CancellationToken cancellationToken);

    Task<UserRecord?> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the user, organization (agency row), founder membership and first workspace in one transaction.
    /// Returns false, and creates nothing, when the email is already registered.
    /// </summary>
    Task<bool> CreateAccountAsync(NewAccount account, CancellationToken cancellationToken);
}
