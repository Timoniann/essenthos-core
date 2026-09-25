using Essenthos.Core.Database.Entities;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading;

/// <summary>
/// One verse of a loaded file, as the file prints it and as its edition does.
/// </summary>
/// <param name="Book">The book, by its canonical ordinal.</param>
/// <param name="Digitised">The verse as the file prints it, markup and all.</param>
/// <param name="Printed">The same verse with the file's faults put right, in the file's own markup.</param>
internal sealed record VerseRepair(int Book, int Chapter, int Verse, string Digitised, string Printed)
{
    public List<VerseToken> Before => VerseWords.Parse(Digitised);

    public List<VerseToken> After => VerseWords.Parse(Printed);
}

/// <summary>
/// What a text's file gets wrong that another witness to the same edition settles, verse by verse,
/// and what the text's row says about it once the words are put right.
/// </summary>
/// <param name="Note">Appended to the text's rights note: which words are ours and what established them.</param>
/// <param name="Source">The copy the words were taken from, where they were taken from one rather than only checked against it.</param>
internal sealed record TextRepairs(
    string Slug,
    IReadOnlyList<VerseRepair> Verses,
    string Note,
    TextPartSource? Source = null)
{
    public static TextRepairs None(string slug) => new(slug, [], string.Empty);

    private readonly Dictionary<(int, int, int), VerseRepair> _byAddress =
        Verses.ToDictionary(repair => (repair.Book, repair.Chapter, repair.Verse));

    /// <summary>The verse as it is to be read: the repaired one where there is a repair, the file's otherwise.</summary>
    public string Text(int book, int chapter, int verse, string digitised) =>
        _byAddress.TryGetValue((book, chapter, verse), out var repair) && repair.Digitised == digitised
            ? repair.Printed
            : digitised;
}

/// <summary>
/// The repairs each bible4u file needs, read where the evidence for them is. The King James's
/// come from the standardised 1769 text eBible publishes, and there are none without it; the
/// Synodal's are a list this project keeps, checked against the Strong-tagged digitisation of the
/// same translation when it was drawn up.
/// </summary>
internal static class Bible4uRepairs
{
    public static TextRepairs For(string identifier, XmlBible.XmlBible bible, string resources) => identifier switch
    {
        "KJV" => KingJamesRepairs.Read(bible, Path.Combine(resources, LostPsalmOpenings.KingJamesFolder)),
        "RUSV" => SynodalCorrections.Read(bible),
        _ => TextRepairs.None(Bible4uTextSource.Definitions[identifier].Slug),
    };
}
