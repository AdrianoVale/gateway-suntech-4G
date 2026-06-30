using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GatewaySunteh4G_NET8.Services.Logging;

public static class DailyFileLoggerExtensions
{
    public static ILoggingBuilder AddDailyFileLogger(this ILoggingBuilder builder)
    {
        builder.Services.AddSingleton<ILoggerProvider, DailyFileLoggerProvider>();
        return builder;
    }
}
