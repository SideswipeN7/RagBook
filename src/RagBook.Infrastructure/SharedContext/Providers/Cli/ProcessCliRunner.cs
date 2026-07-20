using System.Diagnostics;
using System.Text;
using RagBook.Modules.Chat.Domain;

namespace RagBook.Infrastructure.SharedContext.Providers.Cli;

/// <summary>
/// <see cref="ICliRunner"/> over <see cref="Process"/> (US-22). Starts the command with an explicit argument list
/// (never a shell string, so prompt content can't inject commands), writes the payload to stdin, and reads
/// stdout/stderr to completion. On cancellation the process is killed. A start failure (e.g. the binary is not on
/// PATH) surfaces as a non-zero result via the thrown <see cref="Exception"/> caught by the caller.
/// </summary>
public sealed class ProcessCliRunner : ICliRunner
{
    /// <inheritdoc />
    public async Task<CliResult> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string standardInput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        Task readOut = ReadAllAsync(process.StandardOutput, stdout, cancellationToken);
        Task readErr = ReadAllAsync(process.StandardError, stderr, cancellationToken);

        await using (var stdin = process.StandardInput)
        {
            await stdin.WriteAsync(standardInput.AsMemory(), cancellationToken);
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        await Task.WhenAll(readOut, readErr);

        return new CliResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task ReadAllAsync(StreamReader reader, StringBuilder sink, CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            sink.Append(buffer, 0, read);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort — the process may have already exited.
        }
    }
}
