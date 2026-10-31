using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;

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
        services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);

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
}
