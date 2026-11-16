namespace Essenthos.Core.Corpus;

/// <summary>
/// The commentaries the corpus serves beside the verse. Each is somebody's reading of the text, credited on every
/// row it gives and declared once in <see cref="Datasets"/>, where its author, licence and link are.
/// </summary>
public static class CommentaryWorks
{
    /// <param name="Id">How the API and the rows name it.</param>
    /// <param name="Dataset">Its declaration in <see cref="Datasets.All"/>.</param>
    /// <param name="Language">The language it is written in, ISO 639-1.</param>
    /// <param name="Module">The folder under <c>Resources/Commentaries</c> it is in: a SWORD module, or the FictionBook archives of
    /// <see cref="Format.FictionBook"/>.</param>
    /// <param name="Source">The credit on each of its rows, which its declaration claims as its prefix.</param>
    public sealed record Work(string Id, string Dataset, string Language, string Module, string Source, Format Kind = Format.Sword);

    /// <summary>How a commentary's files are written.</summary>
    public enum Format
    {
        /// <summary>A SWORD commentary module, each comment stored at the verses of the King James numbering it is on.</summary>
        Sword,

        /// <summary>
        /// FictionBook books printing the Synodal's verses with the comment on them below, numbered as the Synodal numbers
        /// them; the verses are placed in the shared frame through the Synodal text the corpus holds.
        /// </summary>
        FictionBook,
    }

    public const string HenrySource = "Matthew Henry's Complete Commentary";

    public const string RiegerSource = "Carl Heinrich Rieger, Betrachtungen";

    public const string LopukhinSource = "Толковая Библия, издание А. П. Лопухина и его преемников";

    public static readonly Work[] All =
    [
        new("henry", "henry", "en", "MHC", HenrySource),
        new("rieger", "rieger", "de", "Rieger", RiegerSource),
        new("lopukhin", "lopukhin", "ru", "Lopukhin", LopukhinSource, Format.FictionBook),
    ];

    public static Work? Find(string id) => Array.Find(All, work => work.Id == id);
}
