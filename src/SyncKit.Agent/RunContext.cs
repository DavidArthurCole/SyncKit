using System.Text;

namespace SyncKit.Agent;

// Threads accumulated output and repo path through a pipeline run. Mirrors the Go runCtx. A step reads
// Repo/RepoUrl, appends to Out, runs commands via Run, and records From/To identities for the response.
public sealed class RunContext
{
    public required string Repo { get; init; }
    public required string RepoUrl { get; init; }
    public StringBuilder Out { get; } = new();
    // (name, args) -> (combined stdout+stderr, exitOk). Never throws; a failed command returns ok=false.
    public required Func<string, string[], (string Output, bool Ok)> Run { get; init; }

    public string? FromHash { get; set; }
    public string? ToHash { get; set; }
    public string? FromUrl { get; set; }
    public string? ToUrl { get; set; }
    // Set by a pull step when the pull was a no-op (already up to date) so the pipeline can stop early.
    public bool ShortCircuit { get; set; }
}

// One unit of a deploy pipeline. Returns null on success, or an error message on failure (the message
// is surfaced in the response tail). Mirrors the Go Step interface (Exec returning error).
public interface IStep
{
    string? Exec(RunContext c);

    // True to run this step even after an earlier pull step set RunContext.ShortCircuit (e.g. a
    // shell step that redeploys something unrelated to the pulled artifact, so "the image was
    // already up to date" must not skip it). Defaults false: unrelated steps keep today's
    // short-circuit-stops-the-pipeline behavior.
    bool RunOnShortCircuit => false;
}
