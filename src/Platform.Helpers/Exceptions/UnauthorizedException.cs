namespace Platform.Helpers.Exceptions;

public sealed class UnauthorizedException(string message = "Authentication is required.", string? code = null)
    : AppException(401, message, code);
