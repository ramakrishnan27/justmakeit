using Microsoft.Extensions.DependencyInjection;

namespace Platform.Email;

public static class EmailModule
{
    public static IServiceCollection AddEmailChannel(this IServiceCollection services)
    {
        return services;
    }
}
