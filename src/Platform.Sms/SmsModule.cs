using Microsoft.Extensions.DependencyInjection;

namespace Platform.Sms;

/// <summary>Placeholder for the Sms channel. No logic until the channel is built.</summary>
public static class SmsModule
{
    public static IServiceCollection AddSmsChannel(this IServiceCollection services)
    {
        return services;
    }
}
