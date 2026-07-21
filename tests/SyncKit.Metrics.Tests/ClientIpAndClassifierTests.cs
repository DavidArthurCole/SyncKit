using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SyncKit.Metrics;

namespace SyncKit.Metrics.Tests;

public class ClientIpAndClassifierTests {
    [Fact]
    public void ClientIp_prefersCloudflareHeader() {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["CF-Connecting-IP"] = "5.5.5.5";
        ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        Assert.Equal("5.5.5.5", ClientIp.Resolve(ctx, true));
    }

    [Fact]
    public void ClientIp_usesFirstForwardedHop() {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4, 10.0.0.1";
        Assert.Equal("1.2.3.4", ClientIp.Resolve(ctx, true));
    }

    [Fact]
    public void ClientIp_fallsBackToRemoteAddress() {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");
        Assert.Equal("8.8.8.8", ClientIp.Resolve(ctx, true));
    }

    [Fact]
    public void ClientIp_ignoresHeadersWhenNotBehindProxy() {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["CF-Connecting-IP"] = "5.5.5.5";
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");
        Assert.Equal("8.8.8.8", ClientIp.Resolve(ctx, false));
    }

    [Fact]
    public void Classify_internalMarkerHeaderWins() {
        var classifier = new RequestBucketClassifier(new RequestMetricsOptions());
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Internal"] = "1";
        Assert.Equal(RequestBucket.Internal, classifier.Classify(ctx, ctx.User));
    }

    [Fact]
    public void Classify_authenticatedIsCross() {
        var classifier = new RequestBucketClassifier(new RequestMetricsOptions());
        var ctx = new DefaultHttpContext();
        var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));
        Assert.Equal(RequestBucket.Cross, classifier.Classify(ctx, user));
    }

    [Fact]
    public void Classify_anonymousIsExternal() {
        var classifier = new RequestBucketClassifier(new RequestMetricsOptions());
        var ctx = new DefaultHttpContext();
        Assert.Equal(RequestBucket.External, classifier.Classify(ctx, ctx.User));
    }
}
