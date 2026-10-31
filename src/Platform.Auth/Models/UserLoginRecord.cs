namespace Platform.Auth.Models;

/// <summary>A user as login needs it, including the password hash. Never leaves the Auth project.</summary>
public sealed class UserLoginRecord
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}
