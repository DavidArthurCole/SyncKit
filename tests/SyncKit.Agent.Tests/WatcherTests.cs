using SyncKit.Agent;
using SyncKit.Contract;

namespace SyncKit.Agent.Tests;

public class WatcherTests
{
    private static Watcher New() => new("t", TimeSpan.FromMinutes(1), "http://hook", () => (new DeployResponse(), true));

    [Fact]
    public void Decide_AlreadyUpToDate_Silent() =>
        Assert.Null(New().Decide(new DeployResponse { Ok = true, AlreadyUpToDate = true }));

    [Fact]
    public void Decide_Success_PostsGreenEmbed()
    {
        var payload = New().Decide(new DeployResponse { Ok = true, FromHash = "a", ToHash = "b" });
        Assert.NotNull(payload);
        Assert.Contains("Auto-deployed", payload);
    }

    [Fact]
    public void Decide_Failure_PostsOnceThenDedupes()
    {
        var w = New();
        var fail = new DeployResponse { Ok = false, Tail = "same error" };
        Assert.NotNull(w.Decide(fail));   // first failure posts
        Assert.Null(w.Decide(fail));      // identical tail deduped
    }

    [Fact]
    public void Decide_Failure_ResetsAfterSuccess()
    {
        var w = New();
        var fail = new DeployResponse { Ok = false, Tail = "err" };
        Assert.NotNull(w.Decide(fail));
        Assert.NotNull(w.Decide(new DeployResponse { Ok = true })); // success resets dedupe
        Assert.NotNull(w.Decide(fail));                             // same failure posts again
    }

    [Fact]
    public void Decide_DockerDown_Silent()
    {
        var tail = "Cannot connect to the Docker daemon at unix:///var/run/docker.sock. Is the docker daemon running?";
        Assert.Null(New().Decide(new DeployResponse { Ok = false, Tail = tail }));
    }

    [Fact]
    public void Decide_DockerDown_DoesNotDisturbDedupe()
    {
        var w = New();
        var real = new DeployResponse { Ok = false, Tail = "real build error" };
        var docker = new DeployResponse { Ok = false, Tail = "Cannot connect to the Docker daemon at unix:///var/run/docker.sock." };
        Assert.NotNull(w.Decide(real));   // real failure posts
        Assert.Null(w.Decide(docker));    // docker-down silent, leaves dedupe state alone
        Assert.Null(w.Decide(real));      // same real failure still deduped
    }
}
