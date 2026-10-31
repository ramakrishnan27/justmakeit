namespace Platform.Helpers.Exceptions;

public sealed class ForbiddenException(string message = "You do not have access to this resource.", string? code = null)
    : AppException(403, message, code);
