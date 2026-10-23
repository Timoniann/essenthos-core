namespace Essenthos.Core.Loading;

/// <summary>
/// Whether this run may run a model: SIL's aligner (training or applying it) or UDPipe. Set, a run only
/// reads what earlier runs stored — the alignments in the aligner's workspaces, the ledgers, the
/// generated files. Child processes inherit it.
///
/// <para>
/// <see cref="AlignerAllowed"/> lets the aligner through alone: it is a statistical model trained on the
/// CPU from the corpus's own words, so a corpus rebuilt from nothing can train a pair whose stored
/// answer was made from other words, and every other model stays refused. What it trains goes into a
/// workspace keyed by its inputs, beside the answers other corpora's runs rely on, never over them.
/// </para>
/// </summary>
internal static class ModelRuns
{
    public const string Forbidden = "ESSENTHOS_NO_MODEL_RUNS";

    public const string AlignerAllowed = "ESSENTHOS_ALIGNER_RUNS";

    public static bool Refused => Set(Forbidden);

    public static bool AlignerRefused => Refuses(Environment.GetEnvironmentVariable(Forbidden),
        Environment.GetEnvironmentVariable(AlignerAllowed));

    /// <summary>Whether the aligner is refused, given the two variables as they read.</summary>
    internal static bool Refuses(string? forbidden, string? alignerAllowed) =>
        forbidden is "1" or "true" && alignerAllowed is not ("1" or "true");

    /// <summary>Throws, saying <paramref name="why"/>, if this run may run no model.</summary>
    public static void Refuse(string why)
    {
        if (Refused)
        {
            throw new InvalidOperationException($"{why}, and {Forbidden} is set: this run only reads what earlier runs stored.");
        }
    }

    /// <summary>Throws, saying <paramref name="why"/>, if this run may not train or run the aligner.</summary>
    public static void RefuseAligner(string why)
    {
        if (AlignerRefused)
        {
            throw new InvalidOperationException(
                $"{why}, and {Forbidden} is set without {AlignerAllowed}: this run only reads what earlier runs stored.");
        }
    }

    private static bool Set(string name) => Environment.GetEnvironmentVariable(name) is "1" or "true";
}
