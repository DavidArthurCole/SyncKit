using SyncKit.Agent;

namespace SyncKit.Agent.Tests;

public class ExecutorTests {
    // A step that records it ran and optionally fails / short-circuits, so pipeline flow is testable
    // without real commands.
    private sealed class FakeStep(string? error = null, bool shortCircuit = false, bool runOnShortCircuit = false) : IStep {
        public bool Ran { get; private set; }
        public bool RunOnShortCircuit => runOnShortCircuit;
        public string? Exec(RunContext c) {
            Ran = true;
            c.Out.Append("ran\n");
            if (shortCircuit) c.ShortCircuit = true;
            return error;
        }
    }

    [Fact]
    public void Run_AllSucceed_ReturnsOk() {
        var s1 = new FakeStep();
        var s2 = new FakeStep();
        var res = new Executor { Steps = [s1, s2], Runner = NoRun }.Run();
        Assert.True(res.Ok);
        Assert.False(res.AlreadyUpToDate);
        Assert.True(s1.Ran && s2.Ran);
    }

    [Fact]
    public void Run_StopsAtFirstFailure_TailHasError() {
        var s1 = new FakeStep(error: "boom");
        var s2 = new FakeStep();
        var res = new Executor { Steps = [s1, s2], Runner = NoRun }.Run();
        Assert.False(res.Ok);
        Assert.Contains("boom", res.Tail);
        Assert.False(s2.Ran); // pipeline stopped
    }

    [Fact]
    public void Run_ShortCircuit_ReturnsAlreadyUpToDate_SkipsRest() {
        var s1 = new FakeStep(shortCircuit: true);
        var s2 = new FakeStep();
        var res = new Executor { Steps = [s1, s2], Runner = NoRun }.Run();
        Assert.True(res.Ok);
        Assert.True(res.AlreadyUpToDate);
        Assert.False(s2.Ran);
    }

    [Fact]
    public void Run_ShortCircuit_StillRunsOptedInStep() {
        var s1 = new FakeStep(shortCircuit: true);
        var s2 = new FakeStep();
        var s3 = new FakeStep(runOnShortCircuit: true);
        var res = new Executor { Steps = [s1, s2, s3], Runner = NoRun }.Run();
        Assert.True(res.Ok);
        Assert.True(res.AlreadyUpToDate);
        Assert.False(s2.Ran);
        Assert.True(s3.Ran);
    }

    [Fact]
    public void TailLines_KeepsLastN() =>
        Assert.Equal("c\nd", Executor.TailLines("a\nb\nc\nd\n", 2));

    private static (string, bool) NoRun(string name, string[] args) => ("", true);
}
