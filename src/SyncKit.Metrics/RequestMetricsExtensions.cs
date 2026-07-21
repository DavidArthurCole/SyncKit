using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SyncKit.Metrics;

public static class RequestMetricsExtensions {
    public static IServiceCollection AddSyncKitRequestMetrics(
        this IServiceCollection services, Action<RequestMetricsOptions>? configure = null) {
        var options = new RequestMetricsOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<RequestRateBuffer>();
        services.AddSingleton<RequestAuditLog>();
        services.AddSingleton<RequestBucketClassifier>();
        services.AddSingleton<TrafficReporter>();
        return services;
    }

    public static IApplicationBuilder UseSyncKitRequestMetrics(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestMetricsMiddleware>();
}
