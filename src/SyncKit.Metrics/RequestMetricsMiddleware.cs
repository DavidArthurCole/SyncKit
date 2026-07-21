using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace SyncKit.Metrics;

public sealed class RequestMetricsMiddleware(
    RequestDelegate next,
    RequestRateBuffer rate,
    RequestAuditLog audit,
    RequestBucketClassifier classifier,
    RequestMetricsOptions options) {
    public async Task InvokeAsync(HttpContext ctx) {
        await next(ctx);

        if (!ctx.Request.Path.StartsWithSegments(options.PathPrefix)) return;

        var status = ctx.Response.StatusCode;
        var limited = status == StatusCodes.Status429TooManyRequests;
        rate.Record(limited);

        var bucket = classifier.Classify(ctx, ctx.User);
        var ip = ClientIp.Resolve(ctx, options.HostedBehindProxy);
        var user = ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name : null;
        var method = ctx.Request.Method;
        var path = ctx.Request.Path.Value ?? "";

        var entry = audit.Record(method, path, status, bucket, ip, user);

        var sink = ctx.RequestServices.GetService<IRequestAuditSink>();
        if (sink is not null) await sink.RecordAsync(entry, ctx.RequestAborted);
    }
}
