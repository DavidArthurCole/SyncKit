using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Synckit.Contract;

namespace Synckit.Core;

// Ports Go synckit.NewVersionHandler: bearer-authed POST /events/new-version. Empty secret
// always 401. Constant-time secret compare. Decode failure 400. Handler error 500.
public static class NewVersionHandler
{
    public static RequestDelegate Build(string secret, Func<NewVersionEvent, Task> fn)
    {
        return async ctx =>
        {
            var header = ctx.Request.Headers.Authorization.ToString();
            var token = header.StartsWith("Bearer ", StringComparison.Ordinal)
                ? header["Bearer ".Length..] : header;
            if (string.IsNullOrEmpty(secret) ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(secret)))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("unauthorized");
                return;
            }
            NewVersionEvent? evt;
            try
            {
                evt = await JsonSerializer.DeserializeAsync<NewVersionEvent>(ctx.Request.Body);
            }
            catch (JsonException)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("bad request");
                return;
            }
            if (evt is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("bad request");
                return;
            }
            try
            {
                await fn(evt);
            }
            catch
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync("handler error");
                return;
            }
            ctx.Response.StatusCode = 200;
        };
    }
}
