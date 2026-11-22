namespace Essenthos.Core.Corpus;

/// <summary>
/// The scholarly lexicons the corpus serves beside Strong's dictionary, each keyed by the Strong numbers its articles
/// stand under. Each is credited on every article it gives and declared once in <see cref="Datasets"/>, where its
/// author, licence and link are.
/// </summary>
public static class LexiconWorks
{
    /// <param name="Id">How the API and the rows name it.</param>
    /// <param name="Dataset">Its declaration in <see cref="Datasets.All"/>.</param>
    /// <param name="Language">The language of the numbers it is keyed to: <c>H</c> Hebrew and Aramaic, <c>G</c> Greek.</param>
    /// <param name="Source">The credit on each of its rows, which its declaration claims as its prefix.</param>
    public sealed record Work(string Id, string Dataset, char Language, string Source);

    public const string BdbSource = "Brown-Driver-Briggs, Open Scriptures Hebrew Lexicon";

    public const string ThayerSource = "Thayer, A Greek-English Lexicon of the New Testament";

    public const string AbbottSmithSource = "Abbott-Smith, A Manual Greek Lexicon of the New Testament";

    public const string LsjSource = "Liddell-Scott-Jones, STEPBible TFLSJ";

    public static readonly Work Bdb = new("bdb", "bdb", 'H', BdbSource);

    public static readonly Work Thayer = new("thayer", "thayer", 'G', ThayerSource);

    public static readonly Work AbbottSmith = new("abbott-smith", "abbott-smith", 'G', AbbottSmithSource);

    public static readonly Work Lsj = new("lsj", "stepbible-tflsj", 'G', LsjSource);

    /// <summary>In the order their tabs stand on a Strong page, after Strong's own.</summary>
    public static readonly Work[] All = [Bdb, Thayer, AbbottSmith, Lsj];

    public static Work? Find(string id) => Array.Find(All, work => work.Id == id);
}
