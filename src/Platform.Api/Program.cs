using Platform.Api.Extensions;
using Platform.Api.Middleware;
using Platform.Auth;
using Platform.Email;
using Platform.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure();
builder.Services.AddAuthModule();
builder.Services.AddEmailChannel();
// Adding a channel: fill in its project, then add one line here, for example builder.Services.AddSmsChannel();

builder.Services.AddPlatformApi();

var app = builder.Build();

await app.RunStartupTasksAsync();

app.UseMiddleware<ExceptionHandlingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UsePlatformStaticFiles();
app.UsePlatformCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

// Lets the integration tests host the app with WebApplicationFactory<Program>.
public partial class Program;
