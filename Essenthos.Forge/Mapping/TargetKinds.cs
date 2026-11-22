using System.Text.Json;

namespace Essenthos.Core.Mapping;

/// <summary>What a word of an original is, as far as a middle text's function word may render it (<see cref="MappingLinks.Targets"/>).</summary>
internal enum TargetKind
{
    Other,
    Article,
    Conjunction,
    Adverb,

    /// <summary>A particle, a negation or an interjection.</summary>
    Particle,

    /// <summary>A preposition, BHSA's object marker among them.</summary>
    Preposition,
}

/// <summary>
/// The kind of an original's word from its morphology as the words product holds it: the part of speech of Macula's Nestle
/// 1904 and of BHSA (<c>pos</c>), or the Robinson code of the Textus Receptus (<c>robinson</c>).
/// </summary>
internal static class TargetKinds
{
    public static TargetKind Of(string? morphology)
    {
        if (morphology is null)
        {
            return TargetKind.Other;
        }

        using var json = JsonDocument.Parse(morphology);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return TargetKind.Other;
        }

        if (Field(root, "pos") is { } pos)
        {
            return pos switch
            {
                "det" or "art" => TargetKind.Article,
                "conj" => TargetKind.Conjunction,
                "adv" or "advb" => TargetKind.Adverb,
                "ptcl" or "intj" or "nega" => TargetKind.Particle,
                "prep" => TargetKind.Preposition,
                _ => TargetKind.Other,
            };
        }

        return Field(root, "robinson") switch
        {
            null => TargetKind.Other,
            var code when code.StartsWith("T-", StringComparison.Ordinal) => TargetKind.Article,
            var code when code.StartsWith("CONJ", StringComparison.Ordinal) || code.StartsWith("COND", StringComparison.Ordinal) => TargetKind.Conjunction,
            var code when code.StartsWith("ADV", StringComparison.Ordinal) => TargetKind.Adverb,
            var code when code.StartsWith("PRT", StringComparison.Ordinal) || code == "INJ" => TargetKind.Particle,
            _ => TargetKind.Other,
        };
    }

    private static string? Field(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
