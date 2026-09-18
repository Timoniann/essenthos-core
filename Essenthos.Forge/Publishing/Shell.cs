using System.Diagnostics;
using System.Text;

namespace Essenthos.Core.Publishing;

/// <summary>
/// Runs a program and fails loudly. Publishing is a sequence of other people's tools — docker, ssh,
/// pg_dump, pg_restore, psql — and every one of them reports failure by its exit code, which a caller
/// that forgets to look at turns into a publication that went on after a restore that did not.
/// </summary>
internal static class Shell
{
    public static async Task<string> Run(
        string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool quiet = false)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {program}.");

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{program} {Describe(arguments)} exited with {process.ExitCode}:\n{(await error).Trim()}");
        }

        if (!quiet && (await error).Trim() is { Length: > 0 } warnings)
        {
            Console.Error.WriteLine(warnings);
        }

        return (await output).Trim();
    }

    /// <summary>
    /// A long-running process to be stopped rather than awaited — an ssh tunnel, which lives exactly as
    /// long as the gate that needs it.
    /// </summary>
    public static Process Start(string program, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return Process.Start(start) ?? throw new InvalidOperationException($"Could not start {program}.");
    }

    /// <summary>
    /// One argument as the remote shell will read it. ssh joins its arguments with spaces and hands the
    /// result to a shell on the other side, so an argument with a space or a quote in it — every SQL
    /// statement — arrives split unless it is quoted for that shell.
    /// </summary>
    public static string QuoteForRemote(string argument) =>
        argument.Length > 0 && argument.All(c => char.IsAsciiLetterOrDigit(c) || "-_./=:@".Contains(c))
            ? argument
            : $"'{argument.Replace("'", "'\\''")}'";

    private static string Describe(IReadOnlyList<string> arguments) =>
        string.Join(' ', arguments.Take(6)) + (arguments.Count > 6 ? " …" : string.Empty);
}
