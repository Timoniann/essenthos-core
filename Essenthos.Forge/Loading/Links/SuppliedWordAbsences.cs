using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The editions whose own words — the Almeida's, the American Standard's, the Reina-Valera's and Brenton's italics, the
/// Synodal's and Young's square brackets, the marks the Biblica Ukrainian sets on what it adds — are stated as absent from the
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
///
/// <para>
/// **A mark is relative to the edition's base text** (PRB-1736). Against another witness a run is absent only where that
/// witness has nothing in the verse the base lacks; a variant can print the word (Vaticanus's πρωτον in Ephesians 4:9,
/// which the Biblica Ukrainian marks as added to NA's text). The base is the corpus's text of what the edition translated,
/// or the nearest it holds: BHSA for the Hebrew, the Received Text of 1550 for the Reina-Valera's, the Almeida's, the
/// Synodal's and Young's New Testament, Westcott and Hort's for the revisers' Greek of 1881 under the American Standard, the SBL Greek
/// New Testament for NA's under the Biblica Ukrainian, the Greek Brenton printed beside his translation for Brenton.
/// </para>
/// </summary>
internal static class SuppliedWordAbsences
{
    private const string Hebrew = "hbo";

    private const string Greek = "grc";

    private const string Masoretic = "BHSA";

    private const string ReceivedText = "TR1550";

    /// <param name="OldTestament">The language of the text the edition translated its Old Testament from.</param>
    /// <param name="OldTestamentBase">The corpus's text of what it translated its Old Testament (and deuterocanon) from.</param>
    /// <param name="NewTestamentBase">The same for its New Testament; null for an edition without one.</param>
    internal sealed record Edition(string Slug, string OldTestament, string Source, string OldTestamentBase, string? NewTestamentBase);

    internal static IReadOnlyList<Edition> Editions { get; } =
    [
        new("ALM1911", Hebrew, "Almeida 1911, the words its italics print as the translator's own", Masoretic, ReceivedText),
        new("ASV", Hebrew, "American Standard Version 1901, the words its italics print as the revisers' own", Masoretic, "WH1881"),
        new("RV1909", Hebrew, "Reina-Valera 1909, the words its italics print as the translators' own", Masoretic, ReceivedText),
        new("RUSV", Hebrew, "Russian Synodal, the words its square brackets mark as the translators' own", Masoretic, ReceivedText),
        new("YLT", Hebrew, "Young's Literal Translation 1898, the words its square brackets mark as the translator's own", Masoretic, ReceivedText),
        new("NPU2022", Hebrew, "Biblica Open New Ukrainian Translation 2022, the words it marks as added", Masoretic, "SBLGNT"),
        new("BRENTON", Greek, "Brenton 1844, the words its italics print as the translator's own", "GRCBRENT", null),
    ];
}
