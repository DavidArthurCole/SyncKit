using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Bearer-authed POST /internal/deploy-notify. Same auth contract as NewVersionHandler: empty
// secret 401, bad token 401, decode failure 400, handler error 500. Body is a DeployResponse;
// onDeploy renders and sends it to the configured thread.
public static class DeployNotifyHandler {
    public static RequestDelegate Build(string secret, Func<DeployResponse, Task> onDeploy) {
        return async ctx => {
            if (!await BearerAuth.CheckAsync(ctx, secret)) return;
            DeployResponse? res;
            try {
                res = await JsonSerializer.DeserializeAsync<DeployResponse>(ctx.Request.Body);
            } catch (JsonException) {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("bad request");
                return;
            }
            if (res is null) {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("bad request");
                return;
            }
            try {
                await onDeploy(res);
            } catch {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync("handler error");
                return;
            }
            ctx.Response.StatusCode = 200;
        };
    }
}
