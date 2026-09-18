namespace Essenthos.Core.Corpus;

/// <summary>
/// The few identifiers and provenance strings that the loader writing a row and the surface reading
/// it have to agree on exactly.
///
/// A text's slug is how every other table addresses it, and a source string is what the dataset
/// declaration matches a row's credit against — by prefix, character for character. Declared in one
/// place because the failure when the two sides drift is silent: the rows are written, the
/// declaration finds none of them, and the page says a dataset contributed nothing.
///
/// This is the whole of what the corpus's vocabulary needs from the loaders. Everything else a
/// loader knows is the loader's own.
/// </summary>
internal static class Sources
{
    /// <summary>Brenton's Septuagint, which GLAUx's lexical table annotates.</summary>
    public const string BrentonSeptuagintSlug = "LXX-BRENTON";

    /// <summary>The Kulish Bible, the first complete Ukrainian one.</summary>
    public const string KulishSlug = "UKR1871";

    /// <summary>
    /// What every link drawn from the Synodal's Strong numbering begins with. It names whose
    /// numbering it is and where the copy came from, because the copy carries no credit of its own.
    /// </summary>
    public const string SynodalStrongCredit =
        "the Bob Jones University Strong numbering of the Synodal (1996), read from swmail/RST";

    /// <summary>
    /// How a translated lexicon row begins. The English of the lexicon is public domain and the
    /// rendering into a reader's language is this project's, so the row names the model, the prompt
    /// version and the day, and the English stays beside it.
    /// </summary>
    public const string StrongTranslationPrefix = "a translation of the Strong lexicon by";

    /// <summary>
    /// How a descriptor read off the text begins. The row names the model and the day it was asked,
    /// so the pass is identifiable and removable.
    /// </summary>
    public const string DescriptorReadingPrefix = "read from Scripture by";

    /// <summary>
    /// How a model's reading of a verse begins, for the names no number settles. The row goes on to
    /// name the model, the prompt version and the date of the run.
    /// </summary>
    public const string VerseReadingPrefix = "a reading of the verse by";

    /// <summary>The King James, the Russian Synodal and the Ohienko Ukrainian, as bible4u spells them.</summary>
    public const string KingJamesSlug = "KJV";

    public const string SynodalSlug = "RUSV";

    public const string OhienkoSlug = "UBIO";
}