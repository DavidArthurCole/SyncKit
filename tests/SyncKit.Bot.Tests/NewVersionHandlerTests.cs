using System.Text;
using Microsoft.AspNetCore.Http;
using SyncKit.Bot;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Bot.Tests;

public class NewVersionHandlerTests {
    private static DefaultHttpContext CtxWith(string auth, string body) {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = auth;
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task WrongSecret_401_HandlerNotCalled() {
        var called = false;
        var h = NewVersionHandler.Build("right", _ => { called = true; return Task.CompletedTask; });
        var ctx = CtxWith("Bearer wrong", "{}");
        await h(ctx);
        Assert.Equal(401, ctx.Response.StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task EmptySecret_AlwaysUnauthorized() {
        var h = NewVersionHandler.Build("", _ => Task.CompletedTask);
        var ctx = CtxWith("Bearer anything", "{}");
        await h(ctx);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task GoodSecret_DecodesAndCallsHandler() {
        NewVersionEvent? got = null;
        var h = NewVersionHandler.Build("s3cr3t", e => { got = e; return Task.CompletedTask; });
        var ctx = CtxWith("Bearer s3cr3t",
            "{\"package\":\"com.auxbrain.egginc\",\"version\":\"1.34\",\"apkRef\":\"/x\",\"protoSha\":\"d\",\"detectedAt\":\"t\"}");
        await h(ctx);
        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.NotNull(got);
        Assert.Equal("com.auxbrain.egginc", got!.Package);
    }
}
