namespace Platform.Helpers.Exceptions;

public sealed class ConflictException(string message, string? code = null)
    : AppException(409, message, code);
