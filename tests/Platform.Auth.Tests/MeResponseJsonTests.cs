using System.Text.Json;
using System.Text.Json.Serialization;
using Platform.Auth.DTOs;
using Platform.Auth.Models;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Tests;

/// <summary>
/// How a role and a business model look in the JSON the API sends, serialised with the same enum
/// converter the API registers (camelCase names).
/// </summary>
public sealed class MeResponseJsonTests
{
    private static readonly JsonSerializerOptions Api = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static MeResponse Response(OrganizationRole role, BusinessModel model) =>
        new(
            new UserDto(Guid.NewGuid(), "ana@example.com", "Ana"),
            new AgencyDto(Guid.NewGuid(), "Acme", model, AgencyPlan.Trial),
            role,
            [new MembershipDto(Guid.NewGuid(), "Acme", model, role)]);

    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin)]
    [InlineData(OrganizationRole.ClientManager)]
    [InlineData(OrganizationRole.MarketingManager)]
    [InlineData(OrganizationRole.ContentWriter)]
    public void TheRole_IsWrittenAsTheSameNameTheTokenAndTheDatabaseUse(OrganizationRole role)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Response(role, BusinessModel.Agency), Api));

        Assert.Equal(role.ToDb(), json.RootElement.GetProperty("role").GetString());
        Assert.Equal(role.ToDb(), json.RootElement.GetProperty("memberships")[0].GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(BusinessModel.Company, "company")]
    [InlineData(BusinessModel.Agency, "agency")]
    [InlineData(BusinessModel.Creator, "creator")]
    public void TheBusinessModel_IsWrittenInLowerCase_OnTheOrganizationAndOnEveryMembership(BusinessModel model, string expected)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Response(OrganizationRole.ContentWriter, model), Api));

        Assert.Equal(expected, json.RootElement.GetProperty("agency").GetProperty("businessModel").GetString());
        Assert.Equal(expected, json.RootElement.GetProperty("memberships")[0].GetProperty("businessModel").GetString());
    }

    [Fact]
    public void TheRoleConverter_ReadsTheNameItWrites()
    {
        var back = JsonSerializer.Deserialize<MembershipDto>(
            JsonSerializer.Serialize(new MembershipDto(Guid.NewGuid(), "Acme", BusinessModel.Company, OrganizationRole.MarketingManager), Api),
            Api);

        Assert.Equal(OrganizationRole.MarketingManager, back!.Role);
    }
}
