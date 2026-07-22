using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SyncKit.Contract;

namespace SyncKit.Bot;

public static class NewVersionHandler {
    public static RequestDelegate Build(string secret, Func<NewVersionEvent, Task> fn) {
        return async ctx => {
            if (!await BearerAuth.CheckAsync(ctx, secret)) return;
            NewVersionEvent? evt;
            try {
                evt = await JsonSerializer.DeserializeAsync<NewVersionEvent>(ctx.Request.Body);
            } catch (JsonException) {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("bad request");
                return;
            }
            if (evt is null) {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("bad request");
                return;
            }
            try {
                await fn(evt);
            } catch {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync("handler error");
                return;
            }
            ctx.Response.StatusCode = 200;
        };
    }
}
