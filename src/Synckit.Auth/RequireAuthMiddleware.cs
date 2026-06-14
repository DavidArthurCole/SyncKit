using Microsoft.AspNetCore.Http;

namespace Synckit.Auth;

// Seam over the consumer's sessions table. Returns (found, discordId, expiresAt unix seconds).
public interface ISessionStore
{
    Task<(bool Found, string DiscordId, long ExpiresAt)> LookupAsync(string token, CancellationToken ct);
    Task TouchAsync(string token, long newExpiresAt, CancellationToken ct);
}

// Ports Go auth.RequireAuth. Validates the Bearer token, 401 on missing/unknown/expired,
// else sets the X-Discord-ID header and slides expiry to now+30 days.
public sealed class RequireAuth
{
    private readonly RequestDelegate _next;
    private readonly ISessionStore _store;

    public RequireAuth(RequestDelegate next, ISessionStore store)
    {
        _next = next;
        _store = store;
    }

    // Go strings.TrimPrefix(header, "Bearer "): only strips when the prefix is present.
    public static string ExtractToken(string header) =>
        header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..] : header;

    public async Task Invoke(HttpContext ctx)
    {
        var token = ExtractToken(ctx.Request.Headers.Authorization.ToString());
        if (string.IsNullOrEmpty(token))
        {
            await Unauthorized(ctx);
            return;
        }
        var (found, discordId, expiresAt) = await _store.LookupAsync(token, ctx.RequestAborted);
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!found || nowUnix > expiresAt)
        {
            await Unauthorized(ctx);
            return;
        }
        var slid = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        await _store.TouchAsync(token, slid, ctx.RequestAborted);
        ctx.Request.Headers["X-Discord-ID"] = discordId;
        await _next(ctx);
    }

    private static async Task Unauthorized(HttpContext ctx)
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsync("unauthorized");
    }
}
