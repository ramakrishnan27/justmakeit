namespace Platform.Helpers.Exceptions;

public sealed class NotFoundException(string message = "The requested resource was not found.", string? code = null)
    : AppException(404, message, code);
