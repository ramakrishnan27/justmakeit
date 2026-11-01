namespace Platform.Auth.Interfaces;

public interface ISiteAdminBootstrapper
{
    /// <summary>
    /// Makes the account named by <c>SiteAdmin:Email</c> a site admin, creating it from
    /// <c>SiteAdmin:Password</c> when it does not exist. Does nothing when no email is configured,
    /// and never changes the password of an account that already exists.
    /// </summary>
    Task<SiteAdminBootstrapResult> EnsureAsync(CancellationToken cancellationToken);
}

public enum SiteAdminBootstrapResult
{
    NotConfigured,
    Created,
    Promoted,
    AlreadySiteAdmin,
    MissingPassword
}
