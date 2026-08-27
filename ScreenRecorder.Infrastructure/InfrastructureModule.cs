using Microsoft.Extensions.DependencyInjection;
using ScreenRecorder.Infrastructure.Configuration;

namespace ScreenRecorder.Infrastructure;

/// <summary>Infrastructure 层服务注册（日志/配置/存储/诊断）。</summary>
public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(_ => Logging.LogBootstrap.CreateLogger());
        services.AddSingleton<ConfigService>();
        services.AddSingleton<Diagnostics.SessionMarkerStore>();
        return services;
    }
}
