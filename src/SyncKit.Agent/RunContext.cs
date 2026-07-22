using System.Text;

namespace SyncKit.Agent;

public sealed class RunContext {
    public required string Repo { get; init; }
    public required string RepoUrl { get; init; }
    public StringBuilder Out { get; } = new();
    public required Func<string, string[], (string Output, bool Ok)> Run { get; init; }

    public string? FromHash { get; set; }
    public string? ToHash { get; set; }
    public string? FromUrl { get; set; }
    public string? ToUrl { get; set; }
    public bool ShortCircuit { get; set; }
}

public interface IStep {
    string? Exec(RunContext c);

    bool RunOnShortCircuit => false;
}
