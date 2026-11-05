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
    /// The stored hash of the user's password, or null when they have none (a person who signs in with Google). A user
    /// reads only their own row, so the connection acts as that user.
    /// </summary>
    Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the user's password hash, only if it is still <paramref name="expectedHash"/> (so two changes made at the
    /// same moment cannot overwrite each other), and ends the user's other sessions in the same transaction. False, and
    /// nothing changed, when the stored hash is no longer the expected one.
    /// </summary>
    Task<bool> ChangePasswordAsync(Guid userId, string expectedHash, string newHash, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the user, organization (agency row), founder membership and first workspace in one transaction.
    /// Returns false, and creates nothing, when the email is already registered.
    /// </summary>
    Task<bool> CreateAccountAsync(NewAccount account, CancellationToken cancellationToken);
}
