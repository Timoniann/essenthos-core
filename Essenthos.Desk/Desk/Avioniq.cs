using System.Diagnostics;
using System.Text;

namespace Essenthos.Core.Desk;

/// <param name="Output">What it printed to its standard output.</param>
/// <param name="Error">What it printed to its standard error, which is where it says why it refused.</param>
internal sealed record AvioniqResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// The avioniq command line, run from the control folder. The console asks it which actions it has
/// and runs the ones that apply the owner's changes, the same way a person at a terminal would, so
/// every run is one avioniq lists and nothing here reads or writes the store's files.
/// </summary>
internal sealed class Avioniq(DeskPaths paths, IConfiguration configuration, ILogger<Avioniq> logger)
{
    public const string CommandKey = "Desk:Avioniq";

    /// <summary>How long a query may take before it is given up on, which a board of a few thousand entities never needs.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly Lazy<string[]> _command = new(() => Resolve(configuration));

    public ProcessStartInfo StartInfo(IEnumerable<string> arguments)
    {
        var command = _command.Value;
        var start = new ProcessStartInfo(command[0])
        {
            WorkingDirectory = paths.Workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in command.Skip(1).Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        // Colour codes are for a terminal; the console shows the text as it is.
        start.Environment["NO_COLOR"] = "1";
        start.Environment["FORCE_COLOR"] = "0";
        return start;
    }

    public async Task<AvioniqResult> Run(IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        var start = StartInfo(arguments);
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            logger.LogWarning(exception, "avioniq could not be started as {Command}", start.FileName);
            return new AvioniqResult(-1, string.Empty,
                $"avioniq could not be started ({start.FileName}). Install it, or set {CommandKey} to the command that runs it.");
        }

        process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Patience);
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return new AvioniqResult(-1, string.Empty, $"avioniq did not answer within {Patience.TotalSeconds} seconds.");
        }

        return new AvioniqResult(process.ExitCode, await output, await error);
    }

    /// <summary>
    /// The command that runs avioniq. Configured, it is taken as given. Otherwise on Windows npm's
    /// shim is a batch file, which a process cannot be started from with its arguments passed
    /// intact — an answer holding a quote or an ampersand would be reparsed by cmd — so the script
    /// the shim runs is found beside it and handed to node directly.
    /// </summary>
    private static string[] Resolve(IConfiguration configuration)
    {
        var configured = configuration.GetSection(CommandKey).Get<string[]>();
        if (configured is { Length: > 0 })
        {
            return configured;
        }

        var single = configuration[CommandKey];
        if (!string.IsNullOrWhiteSpace(single))
        {
            return [single];
        }

        if (!OperatingSystem.IsWindows())
        {
            return ["avioniq"];
        }

        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (folder.Length == 0 || !File.Exists(Path.Combine(folder, "avioniq.cmd")))
            {
                continue;
            }

            var script = Path.Combine(folder, "node_modules", "avioniq", "dist", "bin.js");
            if (File.Exists(script))
            {
                var node = Path.Combine(folder, "node.exe");
                return [File.Exists(node) ? node : "node", script];
            }
        }

        return ["avioniq"];
    }
}
