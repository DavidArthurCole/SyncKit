using System.Text;
using Microsoft.AspNetCore.Http;
using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Bot.Tests;

public class DeployNotifyHandlerTests {
    private static DefaultHttpContext CtxWith(string auth, string body) {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = auth;
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task EmptySecret_AlwaysUnauthorized() {
        var h = DeployNotifyHandler.Build("", _ => Task.CompletedTask);
        var ctx = CtxWith("Bearer anything", "{}");
        await h(ctx);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task WrongSecret_401_HandlerNotCalled() {
        var called = false;
        var h = DeployNotifyHandler.Build("right", _ => { called = true; return Task.CompletedTask; });
        var ctx = CtxWith("Bearer wrong", "{}");
        await h(ctx);
        Assert.Equal(401, ctx.Response.StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task GoodSecret_DecodesAndCallsHandler() {
        DeployResponse? got = null;
        var h = DeployNotifyHandler.Build("s3cr3t", r => { got = r; return Task.CompletedTask; });
        var ctx = CtxWith("Bearer s3cr3t", "{\"ok\":true,\"toHash\":\"def\"}");
        await h(ctx);
        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.NotNull(got);
        Assert.True(got!.Ok);
    }

    [Fact]
    public async Task HandlerThrows_500() {
        var h = DeployNotifyHandler.Build("s", _ => throw new InvalidOperationException("boom"));
        var ctx = CtxWith("Bearer s", "{}");
        await h(ctx);
        Assert.Equal(500, ctx.Response.StatusCode);
    }
}
