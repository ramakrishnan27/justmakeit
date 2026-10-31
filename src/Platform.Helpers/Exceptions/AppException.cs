namespace Platform.Helpers.Exceptions;

/// <summary>
/// Base type for errors that should reach the client. The exception middleware turns these into
/// problem responses; any other exception becomes a generic 500 without details.
/// </summary>
public class AppException : Exception
{
    public AppException(int statusCode, string message, string? code = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    /// <summary>Optional machine-readable error code, for example <c>email_taken</c>.</summary>
    public string? Code { get; }
}
