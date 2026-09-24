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

    /// <summary>
    /// Where a sentence ends. A comma or a colon is a pause the parser should see inside a
    /// sentence; these are the marks after which the next word starts a new one.
    /// </summary>
    private static readonly char[] SentenceEnd = ['.', '?', '!'];

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
        var executable = Path.Combine(root, "bin", OperatingSystem.IsWindows() ? "udpipe.exe" : "udpipe");
        var model = Path.Combine(root, "models", modelName);
        if (!File.Exists(executable) || !File.Exists(model))
        {
            // Said in full rather than as one word in a diagnostic line: without it the whole
            // lemma, part-of-speech and morphology layer disappears and every downstream number is
            // quietly the answer for a corpus with no language analysis at all.
            return new UdpipeAnnotation(tokens, UdpipeAnnotationStatus.ToolUnavailable,
                $"no local UDPipe at {executable}" + (File.Exists(executable) ? string.Empty : " (missing)") +
                $" with model {model}" + (File.Exists(model) ? string.Empty : " (missing)") +
                "; run the fetch-udpipe action, and note that the pinned tool is a Windows build.");
        }

        var start = new ProcessStartInfo(executable,
            $"--tag --parse --input horizontal --output conllu \"{model}\"")
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

        // Both readers start before the write: UDPipe produces its output while it is still being
        // fed, and draining one pipe at a time deadlocks as soon as a chapter's output outgrows a
        // pipe buffer.
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteAsync(Sentences(tokens));
        await process.StandardInput.DisposeAsync();
        await Task.WhenAll(output, error);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Local UDPipe exited {process.ExitCode}: {error.Result.Trim()}");
        }

        var lexical = Rows(output.Result).Where(row => Letters(row.Columns[1]).Length > 0).ToList();
        var reconciliation = Reconcile(tokens, [.. lexical.Select(row => row.Columns)]);
        if (reconciliation.ByCorpusIndex is null)
        {
            return new UdpipeAnnotation(tokens, UdpipeAnnotationStatus.TokenMismatch, reconciliation.Detail);
        }

        var heads = Heads(tokens, lexical, reconciliation.CorpusIndexByParsedWord!);
        return new UdpipeAnnotation(
            tokens.Select((token, index) => !reconciliation.ByCorpusIndex.TryGetValue(index, out var columns) ? token : token with
            {
                Lemma = columns[2] == "_" ? token.Lemma : columns[2],
                PartOfSpeech = columns[3] == "_" ? token.PartOfSpeech : columns[3],
                Morphology = Features(columns[5]) ?? token.Morphology,
                SyntacticHead = heads.TryGetValue(index, out var head) ? head : null,
                Relation = columns.Length > 7 && columns[7] != "_" ? columns[7] : null,
            }).ToList(),
            UdpipeAnnotationStatus.Annotated);
    }

    /// <summary>
    /// The passage as sentences, one per line, which is what horizontal input means. The whole
    /// chapter used to arrive as a single line with no punctuation at all - Surface carries none,
    /// the corpus keeps it in Trailer - so Genesis 1 reached the tagger as one 797-token sentence
    /// and a chapter of Leviticus as about 1,500. A lemma survives that; a dependency parse, which
    /// is what the syntax layer was to be grown from, does not.
    ///
    /// The corpus's own tokens are preserved exactly and the trailer's punctuation is written as
    /// tokens of its own, so the model sees the sentence a reader sees without being given the
    /// chance to re-split a word. The reconciler ignores anything with no letters in it.
    /// </summary>
    internal static string Sentences(IReadOnlyList<EvidentiaToken> tokens)
    {
        var sentences = new StringBuilder();
        var current = new List<string>();
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            current.Add(Flatten(token.Surface));
            current.AddRange(token.Trailer.Where(char.IsPunctuation).Select(mark => mark.ToString()));

            var lastOfVerse = index + 1 == tokens.Count || tokens[index + 1].Address != token.Address;
            if (!lastOfVerse && token.Trailer.IndexOfAny(SentenceEnd) < 0)
            {
                continue;
            }

            if (current.Count > 0)
            {
                sentences.Append(string.Join(' ', current)).Append('\n');
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            sentences.Append(string.Join(' ', current)).Append('\n');
        }

        return sentences.ToString();
    }

    private static string Flatten(string surface) =>
        surface.Any(char.IsWhiteSpace) ? string.Concat(surface.Where(character => !char.IsWhiteSpace(character))) : surface;

    internal static ReconciliationResult Reconcile(
        IReadOnlyList<EvidentiaToken> tokens,
        IReadOnlyList<string[]> parsedWords)
    {
        var byCorpusIndex = new Dictionary<int, string[]>();
        var corpusIndexByParsedWord = new int[parsedWords.Count];
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
                corpusIndexByParsedWord[parsedIndex] = corpusIndex;
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
            ? new ReconciliationResult(byCorpusIndex, null, corpusIndexByParsedWord)
            : new ReconciliationResult(null, $"UDPipe left {parsedWords.Count - parsedIndex} lexical tokens unmatched");
    }

    /// <summary>Every word line of the CoNLL-U output, with the sentence it stands in; a word's head is numbered within it.</summary>
    private static List<(int Sentence, string[] Columns)> Rows(string conllu)
    {
        var rows = new List<(int Sentence, string[] Columns)>();
        var sentence = 0;
        foreach (var line in conllu.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                sentence++;
                continue;
            }

            var columns = line.Split('\t');
            if (!line.StartsWith('#') && columns.Length >= 6 && int.TryParse(columns[0], out _))
            {
                rows.Add((sentence, columns));
            }
        }

        return rows;
    }

    /// <summary>
    /// The corpus word each word's syntactic head is, by corpus index. A head that is punctuation, or the
    /// root, is no word of the corpus, and the word then has none.
    /// </summary>
    private static Dictionary<int, long> Heads(
        IReadOnlyList<EvidentiaToken> tokens,
        IReadOnlyList<(int Sentence, string[] Columns)> lexical,
        IReadOnlyList<int> corpusIndexByParsedWord)
    {
        var corpusIndexByWord = new Dictionary<(int Sentence, string Id), int>();
        for (var parsed = 0; parsed < lexical.Count; parsed++)
        {
            corpusIndexByWord[(lexical[parsed].Sentence, lexical[parsed].Columns[0])] = corpusIndexByParsedWord[parsed];
        }

        var heads = new Dictionary<int, long>();
        for (var parsed = 0; parsed < lexical.Count; parsed++)
        {
            var (sentence, columns) = lexical[parsed];
            var corpusIndex = corpusIndexByParsedWord[parsed];
            if (columns.Length > 6 && !heads.ContainsKey(corpusIndex)
                && corpusIndexByWord.TryGetValue((sentence, columns[6]), out var head) && head != corpusIndex)
            {
                heads[corpusIndex] = tokens[head].Id;
            }
        }

        return heads;
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

/// <param name="CorpusIndexByParsedWord">Which corpus word each parsed word was read into.</param>
internal sealed record ReconciliationResult(
    Dictionary<int, string[]>? ByCorpusIndex,
    string? Detail,
    IReadOnlyList<int>? CorpusIndexByParsedWord = null);

internal enum UdpipeAnnotationStatus
{
    Annotated,
    UnsupportedLanguage,
    ToolUnavailable,
    TokenMismatch,
}
