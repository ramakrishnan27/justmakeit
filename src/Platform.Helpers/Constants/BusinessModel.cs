namespace Platform.Helpers.Constants;

/// <summary>
/// The kind of organization that signed up, chosen at registration and never changed afterwards. The
/// lower-cased name is stored in <c>agencies.business_model</c> and <c>agency_members.business_model</c>.
/// </summary>
public enum BusinessModel
{
    /// <summary>One company with its own team and a company workspace.</summary>
    Company,

    /// <summary>A marketing agency that serves several clients, with one workspace per client.</summary>
    Agency,

    /// <summary>A single person who runs their own content: a one-member organization with one workspace.</summary>
    Creator
}
