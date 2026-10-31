using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Middleware;
using Platform.Helpers.Exceptions;

namespace Platform.IntegrationTests.Api;

public sealed class ExceptionHandlingMiddlewareTests
{
    private static async Task<(DefaultHttpContext Context, JsonElement Body)> InvokeAsync(RequestDelegate next)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(context.Response.Body.Length == 0 ? "{}" : await new StreamReader(context.Response.Body).ReadToEndAsync());
        return (context, document.RootElement.Clone());
    }

    [Theory]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not Found")]
    [InlineData(409, "Conflict")]
    public async Task AppException_BecomesAProblemWithItsOwnStatusAndMessage(int status, string title)
    {
        AppException exception = status switch
        {
            400 => new BadRequestException("bad input", "bad_input"),
            401 => new UnauthorizedException("who are you", "no_auth"),
            403 => new ForbiddenException("not yours", "no_access"),
            404 => new NotFoundException("gone", "missing"),
            _ => new ConflictException("already there", "dupe")
        };

        var (context, body) = await InvokeAsync(_ => throw exception);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal(exception.Message, body.GetProperty("detail").GetString());
        Assert.Equal(exception.Code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task BadRequestException_IncludesFieldErrors()
    {
        var errors = new Dictionary<string, string[]> { ["email"] = ["Email is required."] };

        var (_, body) = await InvokeAsync(_ => throw new BadRequestException("Invalid request.", errors: errors));

        Assert.Equal("Email is required.", body.GetProperty("errors").GetProperty("email")[0].GetString());
    }

    [Fact]
    public async Task UnexpectedException_Becomes500WithAGenericMessage_NeverTheExceptionText()
    {
        var (context, body) = await InvokeAsync(_ => throw new InvalidOperationException("secret connection string Host=db;Password=hunter2"));

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("An unexpected error occurred.", body.GetProperty("detail").GetString());
        var raw = body.GetRawText();
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("InvalidOperationException", raw);
        Assert.DoesNotContain("   at ", raw); // no stack trace
    }

    [Fact]
    public async Task BadHttpRequestException_KeepsItsStatusButHidesItsMessage()
    {
        var (context, body) = await InvokeAsync(_ => throw new BadHttpRequestException("internal parser detail", 413));

        Assert.Equal(413, context.Response.StatusCode);
        Assert.Equal("The request was malformed.", body.GetProperty("detail").GetString());
        Assert.DoesNotContain("internal parser detail", body.GetRawText());
    }

    [Fact]
    public async Task RequestAbortedByTheClient_WritesNoResponseBody()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new OperationCanceledException(aborted.Token), NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task RequestThatSucceeds_IsLeftAlone()
    {
        var (context, _) = await InvokeAsync(ctx =>
        {
            ctx.Response.StatusCode = 204;
            return Task.CompletedTask;
        });

        Assert.Equal(204, context.Response.StatusCode);
    }

    [Fact]
    public async Task WhenTheResponseHasAlreadyStarted_TheMiddlewareLeavesItAlone()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(
            async ctx =>
            {
                await ctx.Response.WriteAsync("partial");
                throw new NotFoundException();
            },
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var exception = await Record.ExceptionAsync(() => middleware.InvokeAsync(context));

        Assert.Null(exception);
        context.Response.Body.Position = 0;
        Assert.Equal("partial", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
