using Microsoft.AspNetCore.Http;

namespace SyncKit.Metrics;

public static class ClientIp {
    public static string Resolve(HttpContext ctx, bool hostedBehindProxy) {
        if (hostedBehindProxy) {
            var cf = ctx.Request.Headers["CF-Connecting-IP"].ToString();
            if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();

            var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(xff)) {
                var first = xff.Split(',', 2)[0].Trim();
                if (first.Length > 0) return first;
            }
        }
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
