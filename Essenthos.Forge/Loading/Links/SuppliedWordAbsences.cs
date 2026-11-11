using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The editions whose own words — the Almeida's, the American Standard's, the Reina-Valera's and Brenton's italics, the
/// Synodal's square brackets, the marks the Biblica Ukrainian sets on what it adds — are stated as absent from the
/// originals they translate, the way the King James' italics are: an <see cref="LinkRelation.Expands"/> link with nothing
/// on the witness's side, one per marked run of words in a verse (<see cref="Mapping.SuppliedWordSource"/>).
///
/// <para>
/// A mark is a statement about the edition's base text, so the witnesses are those in its language:
/// the Hebrew for the Old Testament and the Greek for the New, and the Greek throughout for Brenton,
/// who translated the Vatican text. The Synodal's brackets also hold readings it took from the
/// Septuagint where the Hebrew has none; against the Hebrew those words are absent too, and they are
/// not stated against the Greek.
/// </para>
/// </summary>
internal static class SuppliedWordAbsences
{
    private const string Hebrew = "hbo";

    private const string Greek = "grc";

    /// <param name="OldTestament">The language of the text the edition translated its Old Testament from.</param>
    internal sealed record Edition(string Slug, string OldTestament, string Source);

    internal static IReadOnlyList<Edition> Editions { get; } =
    [
        new("ALM1911", Hebrew, "Almeida 1911, the words its italics print as the translator's own"),
        new("ASV", Hebrew, "American Standard Version 1901, the words its italics print as the revisers' own"),
        new("RV1909", Hebrew, "Reina-Valera 1909, the words its italics print as the translators' own"),
        new("RUSV", Hebrew, "Russian Synodal, the words its square brackets mark as the translators' own"),
        new("NPU2022", Hebrew, "Biblica Open New Ukrainian Translation 2022, the words it marks as added"),
        new("BRENTON", Greek, "Brenton 1844, the words its italics print as the translator's own"),
    ];
}
