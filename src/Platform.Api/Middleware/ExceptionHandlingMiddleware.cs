using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Platform.Helpers.Exceptions;

namespace Platform.Api.Middleware;

/// <summary>
/// Turns exceptions into RFC 9457 problem responses. <see cref="AppException"/> keeps its status and
/// message; anything else is logged and becomes a generic 500 with no details for the client.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private const string ProblemJson = "application/problem+json";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
        }
        catch (AppException exception)
        {
            logger.LogInformation(
                "Request to {Route} rejected with {Status}: {Message}", RouteOf(context), exception.StatusCode, exception.Message);
            var errors = (exception as BadRequestException)?.Errors;
            await WriteProblemAsync(context, exception.StatusCode, exception.Message, exception.Code, errors);
        }
        catch (BadHttpRequestException exception)
        {
            logger.LogInformation("Request to {Route} was malformed: {Message}", RouteOf(context), exception.Message);
            await WriteProblemAsync(context, exception.StatusCode, "The request was malformed.", "bad_request", null);
        }
        catch (Exception exception)
        {
            // Never send the exception text or stack trace to the client.
            logger.LogError(exception, "Unhandled exception for {Method} {Route}", context.Request.Method, RouteOf(context));
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "server_error", null);
        }
    }

    // Public routes such as /t/c/{token} and /u/{token} carry secrets in the path, so log the route
    // template, never the concrete path.
    private static string RouteOf(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unrouted)";

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string detail,
        string? code,
        IReadOnlyDictionary<string, string[]>? errors)
    {
        if (context.Response.HasStarted)
        {
            // Too late to change the response; the connection is aborted by the server.
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }

        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: ProblemJson, cancellationToken: context.RequestAborted);
    }
}
