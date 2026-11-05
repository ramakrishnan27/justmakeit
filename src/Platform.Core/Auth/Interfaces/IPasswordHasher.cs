namespace Platform.Auth.Interfaces;

public interface IPasswordHasher
{
    /// <summary>Returns <c>pbkdf2$iterations$salt$hash</c> for a new random salt.</summary>
    string Hash(string password);

    /// <summary>Checks a password against a stored hash in constant time. A malformed hash never matches.</summary>
    bool Verify(string password, string storedHash);

    /// <summary>
    /// A valid hash of a random password nobody knows. Login verifies against it when the email is
    /// unknown, so an unknown email takes as long as a wrong password and cannot be told apart by timing.
    /// </summary>
    string DummyHash { get; }
}
