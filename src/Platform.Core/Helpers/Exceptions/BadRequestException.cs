namespace Platform.Helpers.Exceptions;

public sealed class BadRequestException : AppException
{
    public BadRequestException(string message, string? code = null, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(400, message, code)
    {
        Errors = errors;
    }

    /// <summary>Per-field validation messages, keyed by field name.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }
}
