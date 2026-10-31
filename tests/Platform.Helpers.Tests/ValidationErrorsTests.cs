using Platform.Helpers.Exceptions;
using Platform.Helpers.Validation;

namespace Platform.Helpers.Tests;

public sealed class ValidationErrorsTests
{
    [Fact]
    public void ANewCollection_HasNoErrors_AndThrowsNothing()
    {
        var errors = new ValidationErrors();

        Assert.False(errors.Any);
        Assert.Empty(errors.ToDictionary());
        errors.ThrowIfAny();
    }

    [Fact]
    public void Add_GroupsMessagesByField()
    {
        var errors = new ValidationErrors()
            .Add("email", "Email is required.")
            .Add("email", "Enter a valid email address.")
            .Add("password", "Password is required.");

        var dictionary = errors.ToDictionary();

        Assert.True(errors.Any);
        Assert.Equal(["Email is required.", "Enter a valid email address."], dictionary["email"]);
        Assert.Equal(["Password is required."], dictionary["password"]);
    }

    [Fact]
    public void ThrowIfAny_RaisesA400WithTheCodeAndFieldMessages()
    {
        var errors = new ValidationErrors().Add("name", "Name is required.");

        var exception = Assert.Throws<BadRequestException>(errors.ThrowIfAny);

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(ValidationErrors.Code, exception.Code);
        Assert.Equal(["Name is required."], exception.Errors!["name"]);
    }

    [Fact]
    public void ToDictionary_ReturnsACopy_SoLaterAddsDoNotChangeIt()
    {
        var errors = new ValidationErrors().Add("a", "first");
        var snapshot = errors.ToDictionary();

        errors.Add("a", "second");

        Assert.Equal(["first"], snapshot["a"]);
    }

    [Theory]
    [InlineData("", "message")]
    [InlineData("  ", "message")]
    [InlineData("field", "")]
    [InlineData("field", "  ")]
    public void Add_RejectsABlankFieldOrMessage(string field, string message)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ValidationErrors().Add(field, message));
    }
}
