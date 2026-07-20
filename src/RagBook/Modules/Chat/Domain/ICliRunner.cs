namespace RagBook.Modules.Chat.Domain;

/// <summary>The outcome of a CLI invocation.</summary>
/// <param name="ExitCode">The process exit code (0 = success).</param>
/// <param name="StdOut">Captured standard output.</param>
/// <param name="StdErr">Captured standard error.</param>
public readonly record struct CliResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Runs an external command with an explicit argument list (no shell string — so caller-supplied prompt content can
/// never inject shell commands, US-22) and a stdin payload, capturing stdout/stderr. The seam lets the CLI answer
/// generator be unit-tested with a fake runner (no real process, §IV).
/// </summary>
public interface ICliRunner
{
    /// <summary>
    /// Runs <paramref name="command"/> with <paramref name="arguments"/>, writing <paramref name="standardInput"/> to
    /// its stdin, and returns the exit code + captured output. Times out / cancels via <paramref name="cancellationToken"/>.
    /// </summary>
    Task<CliResult> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string standardInput,
        CancellationToken cancellationToken);
}
