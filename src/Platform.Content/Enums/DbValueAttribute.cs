namespace Platform.Content.Enums;

/// <summary>
/// The text a member of an enum is stored and sent as: the snake_case value of the CHECK constraints in
/// <c>014_content_studio.sql</c> (the shared <c>ToDb()</c> of the infrastructure lower-cases the member name, which would give
/// <c>socialpost</c> for <c>SocialPost</c> and <c>youtube</c> only by luck).
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class DbValueAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}
