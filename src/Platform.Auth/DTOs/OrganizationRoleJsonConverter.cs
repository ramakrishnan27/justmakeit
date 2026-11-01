using System.Text.Json;
using System.Text.Json.Serialization;
using Platform.Helpers.Constants;

namespace Platform.Auth.DTOs;

/// <summary>
/// Writes a role the way the database and the access token's role claim spell it
/// (<c>organizationadmin</c>), so a client compares one spelling everywhere. The API's general enum
/// converter would write <c>organizationAdmin</c>. Put it on the property: a converter in the
/// serializer options would win over one on the type.
/// </summary>
public sealed class OrganizationRoleJsonConverter()
    : JsonStringEnumConverter<OrganizationRole>(new LowerCaseNamingPolicy())
{
    private sealed class LowerCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => name.ToLowerInvariant();
    }
}
