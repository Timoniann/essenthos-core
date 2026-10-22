namespace Essenthos.Core.Loading;

/// <summary>
/// Whether this run may run a model: SIL's aligner (training or applying it) or UDPipe. Set, a run only
/// reads what earlier runs stored — the alignments in the aligner's workspaces, the ledgers, the
/// generated files — which is what a corpus rebuilt from nothing asks: it ends where the stored answers
/// are, and never trains, clears or rewrites the workspaces another corpus's runs rely on. Child
/// processes inherit it.
/// </summary>
internal static class ModelRuns
{
    public const string Forbidden = "ESSENTHOS_NO_MODEL_RUNS";

    public static bool Refused => Environment.GetEnvironmentVariable(Forbidden) is "1" or "true";

    /// <summary>Throws, saying <paramref name="why"/>, if this run may run no model.</summary>
    public static void Refuse(string why)
    {
        if (Refused)
        {
            throw new InvalidOperationException($"{why}, and {Forbidden} is set: this run only reads what earlier runs stored.");
        }
    }
}
