using System.Diagnostics.CodeAnalysis;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Services;

/// <summary>
/// What each business model allows, in one place. <c>003_business_models.sql</c> has the same table as
/// a CHECK constraint, so the database refuses a pair that is not listed here; an integration test
/// compares the two for every combination.
/// </summary>
public static class BusinessModelRules
{
    private static readonly OrganizationRole[] CompanyRoles =
        [OrganizationRole.OrganizationAdmin, OrganizationRole.MarketingManager, OrganizationRole.ContentWriter];

    private static readonly OrganizationRole[] AgencyRoles =
    [
        OrganizationRole.OrganizationAdmin, OrganizationRole.ClientManager,
        OrganizationRole.MarketingManager, OrganizationRole.ContentWriter
    ];

    private static readonly OrganizationRole[] CreatorRoles = [OrganizationRole.ContentWriter];

    /// <summary>The roles an organization of this model may give its members.</summary>
    public static IReadOnlyList<OrganizationRole> AllowedRoles(this BusinessModel model) => model switch
    {
        BusinessModel.Company => CompanyRoles,
        BusinessModel.Agency => AgencyRoles,
        BusinessModel.Creator => CreatorRoles,
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown business model.")
    };

    public static bool IsAllowed(this BusinessModel model, OrganizationRole role) => model.AllowedRoles().Contains(role);

    /// <summary>
    /// The role of the person who registers the organization. A company or agency starts with its
    /// administrator; an individual creator is the only member and works as a content writer.
    /// </summary>
    public static OrganizationRole FounderRole(this BusinessModel model) => model switch
    {
        BusinessModel.Company or BusinessModel.Agency => OrganizationRole.OrganizationAdmin,
        BusinessModel.Creator => OrganizationRole.ContentWriter,
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown business model.")
    };

    /// <summary>An individual creator has the one workspace made at sign-up; the others may add more.</summary>
    public static bool AllowsSeveralWorkspaces(this BusinessModel model) => model is not BusinessModel.Creator;

    /// <summary>
    /// Reads a model from its stored lower-case name, such as a registration request. Strict on
    /// purpose: anything but the exact name, including a number, is not a model.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out BusinessModel model)
    {
        foreach (var candidate in Enum.GetValues<BusinessModel>())
        {
            if (value == candidate.ToDb())
            {
                model = candidate;
                return true;
            }
        }

        model = default;
        return false;
    }
}
