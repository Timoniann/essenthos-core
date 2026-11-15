using System.Globalization;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// How a verdict's claims are written: the rule's source and evidence, a person's source, and the notes a claim on a wider
/// link carries. The ledger's verdicts reach the build as method files (<see cref="Mapping.EvidentiaFiles"/>), which write
/// them so; no verb writes a verdict into a corpus database any more (PRB-1597).
/// </summary>
internal static class EvidentiaClaims
{
    /// <summary>The source of the rule's claim: the run, the rules' version and the kind of decision.</summary>
    internal static string RuleSource(int run, string ruleVersion, string kind) =>
        $"{Sources.EvidentiaRunPrefix} {run} ({ruleVersion}), {kind}";

    /// <summary>The note of a verdict's claim on a link naming more words than the verdict's pair.</summary>
    internal const string WithinNote = "names one word pair of this link";

    /// <summary>The note of a verdict's claim on an absence naming more words than the verdict's.</summary>
    internal const string WithinAbsenceNote = "names one word of this absence";

    /// <summary>The run a rule's claim names in its source, as <see cref="RuleSource"/> writes it; null for any other source.</summary>
    internal static int? RunOf(string source)
    {
        const string prefix = Sources.EvidentiaRunPrefix + " ";
        if (!source.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = source.AsSpan(prefix.Length);
        var end = rest.IndexOf(' ');
        return int.TryParse(end < 0 ? rest : rest[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    /// <summary>
    /// What a rule's claim says of its decision beyond the decision's row: the tier, the rationale, and
    /// whether the tier was accepted unread.
    /// </summary>
    internal static string RuleEvidence(string? tier, string? rationale, bool examined, string reviewer) =>
        examined ? $"{tier} tier: {rationale}" : $"{tier} tier: {rationale}; accepted with its tier by {reviewer}, not read one by one";

    /// <summary>The source of a person's claim on a verdict.</summary>
    internal static string PersonSource(string reviewer, int run, bool corrected) =>
        corrected ? $"{reviewer}, correcting EVIDENTIA run {run}" : $"{reviewer}, reviewing EVIDENTIA run {run}";
}
