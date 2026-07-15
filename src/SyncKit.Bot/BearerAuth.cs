using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace SyncKit.Bot;

// Constant-time bearer-secret gate. Empty secret always fails. Writes 401 + "unauthorized" on
// failure so callers can early-return.
internal static class BearerAuth {
    public static async Task<bool> CheckAsync(HttpContext ctx, string secret) {
        var header = ctx.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.Ordinal)
            ? header["Bearer ".Length..] : header;
        if (string.IsNullOrEmpty(secret) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(secret))) {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsync("unauthorized");
            return false;
        }
        return true;
    }
}
