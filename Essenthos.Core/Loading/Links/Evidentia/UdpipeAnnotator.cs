using System.Diagnostics;
using System.Text;
using Essenthos.Core.Configuration;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>Applies an audited local UDPipe model without modifying corpus rows.</summary>
internal sealed class UdpipeAnnotator(IConfiguration configuration, IHostEnvironment environment)
{
    private static readonly IReadOnlyDictionary<string, string> Models =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["eng"] = "english-ud-2.1-20180111.udpipe",
            ["rus"] = "russian-syntagrus-ud-2.5-191206.udpipe",
            ["ukr"] = "ukrainian-iu-ud-2.5-191206.udpipe",
        };

    public async Task<UdpipeAnnotation> Annotate(
        IReadOnlyList<EvidentiaToken> tokens,
        CancellationToken cancellationToken)
    {
        var languages = tokens.Select(token => token.Language).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (languages.Count != 1 || !Models.TryGetValue(languages[0], out var modelName))
        {
            return new UdpipeAnnotation(tokens, UdpipeAnnotationStatus.UnsupportedLanguage);
        }

        var root = Path.Combine(ResourcePaths.Read(configuration, environment.ContentRootPath), "UDPipe");
        var executable = Path.Combine(root, "bin", "udpipe.exe");
        var model = Path.Combine(root, "models", modelName);
        if (!File.Exists(executable) || !File.Exists(model))
        {
            return new UdpipeAnnotation(tokens, UdpipeAnnotationStatus.ToolUnavailable);
        }

        var start = new ProcessStartInfo(executable,
            $"--tokenize --tag --parse --input horizontal --output conllu \"{model}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start local UDPipe; reinstall Resources/UDPipe with fetch-udpipe.");
        await process.StandardInput.WriteAsync(string.Join(' ', tokens.Select(token => token.Surface)));
        await process.StandardInput.DisposeAsync();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Local UDPipe exited {process.ExitCode}: {error.Trim()}");
        }

        var analysis = output.Split('\n')
            .Where(line => !line.StartsWith('#') && !string.IsNullOrWhiteSpace(line))
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(columns => columns.Length >= 6 && int.TryParse(columns[0], out _))
            .ToList();
        var reconciliation = Reconcile(tokens, analysis.Where(columns => Letters(columns[1]).Length > 0).ToList());
        if (reconciliation.ByCorpusIndex is null)
        {
            return new UdpipeAnnotation(tokens, UdpipeAnnotationStatus.TokenMismatch, reconciliation.Detail);
        }

        return new UdpipeAnnotation(
            tokens.Select((token, index) => !reconciliation.ByCorpusIndex.TryGetValue(index, out var columns) ? token : token with
            {
                Lemma = columns[2] == "_" ? token.Lemma : columns[2],
                PartOfSpeech = columns[3] == "_" ? token.PartOfSpeech : columns[3],
                Morphology = Features(columns[5]) ?? token.Morphology,
            }).ToList(),
            UdpipeAnnotationStatus.Annotated);
    }

    private static ReconciliationResult Reconcile(
        IReadOnlyList<EvidentiaToken> tokens,
        IReadOnlyList<string[]> parsedWords)
    {
        var byCorpusIndex = new Dictionary<int, string[]>();
        var parsedIndex = 0;
        for (var corpusIndex = 0; corpusIndex < tokens.Count; corpusIndex++)
        {
            var expected = Letters(tokens[corpusIndex].Surface);
            if (expected.Length == 0)
            {
                continue;
            }

            var actual = string.Empty;
            string[]? firstParsedPart = null;
            while (actual.Length < expected.Length && parsedIndex < parsedWords.Count)
            {
                var parsedPart = parsedWords[parsedIndex++];
                actual += Letters(parsedPart[1]);
                firstParsedPart ??= parsedPart;
            }

            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                return new ReconciliationResult(null,
                    $"corpus #{corpusIndex + 1} '{tokens[corpusIndex].Surface}' ≠ UDPipe '{actual}'");
            }

            byCorpusIndex[corpusIndex] = firstParsedPart!;
        }

        return parsedIndex == parsedWords.Count
            ? new ReconciliationResult(byCorpusIndex, null)
            : new ReconciliationResult(null, $"UDPipe left {parsedWords.Count - parsedIndex} lexical tokens unmatched");
    }

    private static IReadOnlyDictionary<string, string>? Features(string value)
    {
        if (value == "_")
        {
            return null;
        }

        return value.Split('|')
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);
    }

    private static string Letters(string value) => string.Concat(value.Where(char.IsLetter));
}

internal sealed record UdpipeAnnotation(
    IReadOnlyList<EvidentiaToken> Tokens,
    UdpipeAnnotationStatus Status,
    string? Detail = null);

internal sealed record ReconciliationResult(
    Dictionary<int, string[]>? ByCorpusIndex,
    string? Detail);

internal enum UdpipeAnnotationStatus
{
    Annotated,
    UnsupportedLanguage,
    ToolUnavailable,
    TokenMismatch,
}
