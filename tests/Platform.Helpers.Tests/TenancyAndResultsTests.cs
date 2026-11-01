using Platform.Helpers.Exceptions;
using Platform.Helpers.Results;
using Platform.Helpers.Tenancy;

namespace Platform.Helpers.Tests;

public sealed class TenantContextTests
{
    [Fact]
    public void NewContext_IsEmpty()
    {
        var tenant = new TenantContext();

        Assert.Null(tenant.AgencyId);
        Assert.Null(tenant.UserId);
        Assert.Null(tenant.Role);
    }

    [Fact]
    public void Set_StoresAllThreeValues()
    {
        var agency = Guid.NewGuid();
        var user = Guid.NewGuid();
        var tenant = new TenantContext();

        tenant.Set(agency, user, "organizationadmin");

        Assert.Equal(agency, tenant.AgencyId);
        Assert.Equal(user, tenant.UserId);
        Assert.Equal("organizationadmin", tenant.Role);
        Assert.Equal(agency, tenant.RequireAgencyId());
        Assert.Equal(user, tenant.RequireUserId());
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var tenant = new TenantContext();
        tenant.Set(Guid.NewGuid(), Guid.NewGuid(), "organizationadmin");

        tenant.Clear();

        Assert.Null(tenant.AgencyId);
        Assert.Null(tenant.UserId);
        Assert.Null(tenant.Role);
    }

    [Fact]
    public void RequireAgencyId_WithoutAgency_ThrowsUnauthorized()
    {
        Assert.Throws<UnauthorizedException>(() => new TenantContext().RequireAgencyId());
    }

    [Fact]
    public void RequireUserId_WithoutUser_ThrowsUnauthorized()
    {
        var tenant = new TenantContext();
        tenant.Set(Guid.NewGuid(), null);

        Assert.Throws<UnauthorizedException>(() => tenant.RequireUserId());
    }
}

public sealed class ExceptionTests
{
    [Fact]
    public void EachSubclass_CarriesItsHttpStatus()
    {
        Assert.Equal(400, new BadRequestException("x").StatusCode);
        Assert.Equal(401, new UnauthorizedException().StatusCode);
        Assert.Equal(403, new ForbiddenException().StatusCode);
        Assert.Equal(404, new NotFoundException().StatusCode);
        Assert.Equal(409, new ConflictException("x").StatusCode);
    }

    [Fact]
    public void AllSubclasses_AreAppExceptions()
    {
        AppException[] all = [new BadRequestException("x"), new UnauthorizedException(), new ForbiddenException(), new NotFoundException(), new ConflictException("x")];

        Assert.All(all, e => Assert.IsAssignableFrom<AppException>(e));
    }

    [Fact]
    public void Code_IsOptionalAndPreserved()
    {
        Assert.Null(new NotFoundException().Code);
        Assert.Equal("email_taken", new ConflictException("taken", "email_taken").Code);
    }

    [Fact]
    public void BadRequestException_CarriesFieldErrors()
    {
        var errors = new Dictionary<string, string[]> { ["email"] = ["Required"] };

        var exception = new BadRequestException("Invalid", errors: errors);

        Assert.Equal(["Required"], exception.Errors!["email"]);
    }
}

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0, 25, 0)]
    [InlineData(1, 25, 1)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    [InlineData(100, 10, 10)]
    [InlineData(5, 0, 0)]
    public void TotalPages_RoundsUp(long total, int pageSize, int expectedPages)
    {
        var result = new PagedResult<int> { Items = [], Page = 1, PageSize = pageSize, TotalCount = total };

        Assert.Equal(expectedPages, result.TotalPages);
    }
}
