using System.Diagnostics;
using System.Text;
using SyncKit.Contract;

namespace SyncKit.Agent;

// Runs an ordered list of steps and produces a DeployResponse. Mirrors the Go Executor: stop at the
// first failure, accumulate output, track from/to identities, return a 20-line tail on failure.
public sealed class Executor
{
    public string Repo { get; init; } = "";
    public string RepoUrl { get; init; } = "";
    public required IReadOnlyList<IStep> Steps { get; init; }
    public Func<string, string[], (string Output, bool Ok)> Runner { get; init; } = RealRunner;

    public DeployResponse Run()
    {
        var c = new RunContext { Repo = Repo, RepoUrl = RepoUrl, Run = Runner };
        foreach (var step in Steps)
        {
            var err = step.Exec(c);
            if (err is not null)
            {
                // The error message is often the only signal, so it must survive the tail cut.
                c.Out.Append('\n').Append(err).Append('\n');
                return new DeployResponse
                {
                    FromHash = c.FromHash, ToHash = c.ToHash, FromUrl = c.FromUrl, ToUrl = c.ToUrl,
                    Tail = TailLines(c.Out.ToString(), 20),
                };
            }
            if (c.ShortCircuit)
                return new DeployResponse
                {
                    Ok = true, AlreadyUpToDate = true,
                    FromHash = c.FromHash, ToHash = c.ToHash, FromUrl = c.FromUrl, ToUrl = c.ToUrl,
                };
        }
        return new DeployResponse { Ok = true, FromHash = c.FromHash, ToHash = c.ToHash, FromUrl = c.FromUrl, ToUrl = c.ToUrl };
    }

    // Runs a command, returns (combined stdout+stderr, exitCode==0). Never throws: a spawn failure
    // returns ok=false with the exception text, so a step decides how to report it.
    public static (string Output, bool Ok) RealRunner(string name, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(name)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return ($"failed to start {name}", false);
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (stdout + stderr, p.ExitCode == 0);
        }
        catch (Exception e) { return ($"{name}: {e.Message}", false); }
    }

    internal static string TailLines(string s, int n)
    {
        if (s == "" || n <= 0) return "";
        var lines = s.TrimEnd('\r', '\n').Split('\n');
        if (lines.Length <= n) return string.Join("\n", lines);
        return string.Join("\n", lines[^n..]);
    }
}
