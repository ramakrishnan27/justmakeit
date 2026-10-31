using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;

namespace Platform.Api.Extensions;

public static class ApiServiceCollectionExtensions
{
    /// <summary>Host-level services that belong to no single module: MVC, CORS, rate limiting, Swagger.</summary>
    public static IServiceCollection AddPlatformApi(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        services.AddCors();

        // Modules add their own named policies (for example the /v1/auth/* limits) to this limiter.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRateLimitProblemAsync;
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Platform API", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Access token returned by POST /v1/auth/login."
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
            });
        });

        return services;
    }

    // Same problem+json shape as every other error, plus Retry-After so a client knows when to try again.
    private static async ValueTask WriteRateLimitProblemAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.CacheControl = "no-store";

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = ReasonPhrases.GetReasonPhrase(StatusCodes.Status429TooManyRequests),
            Detail = "Too many requests. Wait a moment and try again."
        };
        problem.Extensions["code"] = "rate_limited";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        await response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
    }
}
