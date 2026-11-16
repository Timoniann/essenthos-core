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
    /// <param name="Module">The folder under <c>Resources/Commentaries</c> its SWORD module is in.</param>
    /// <param name="Source">The credit on each of its rows, which its declaration claims as its prefix.</param>
    public sealed record Work(string Id, string Dataset, string Language, string Module, string Source);

    public const string HenrySource = "Matthew Henry's Complete Commentary";

    public const string RiegerSource = "Carl Heinrich Rieger, Betrachtungen";

    public static readonly Work[] All =
    [
        new("henry", "henry", "en", "MHC", HenrySource),
        new("rieger", "rieger", "de", "Rieger", RiegerSource),
    ];

    public static Work? Find(string id) => Array.Find(All, work => work.Id == id);
}
