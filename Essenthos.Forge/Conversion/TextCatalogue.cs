using Essenthos.Core.Configuration;
using Essenthos.Core.Loading;
using Essenthos.Core.Bhsa;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Conversion;

/// <summary>
/// One text the converter makes: its slug, the sources under Resources/ it reads (whose hashes decide
/// whether it is made again), and how its words are read from them. The readers are the load's own
/// <c>*TextSource</c> classes; a text corrected at its source (bible4u's three) is read with the
/// repairs its corrections file declares instead of the ones the load computes.
/// </summary>
internal sealed record CataloguedText(
    string Slug,
    IReadOnlyList<string> Sources,
    Func<string, TextCorrections, TextSource> Read);

internal static class TextCatalogue
{
    private static readonly string[] Bible4uExtended =
    [
        DeuterocanonTextSource.KingJamesFolder, DeuterocanonTextSource.SynodalFolder, DeuterocanonTextSource.WorldEnglishFolder,
    ];

    /// <summary>
    /// The second reading of Swete's and Ottley's text, made from the printed pages: the readings and verdicts the
    /// tables their readers apply (<c>Swete/SweteSettled.json</c>, <c>Swete/OttleyPage.json</c>) are written from. A
    /// change to them makes the texts again.
    /// </summary>
    private const string SwetePages = "Essenthos/generation/pages-swete-2026-10-10/out";

    private static readonly string[] OttleyPages =
    [
        "Essenthos/generation/pages-ottley-2026-10-09/settled.json",
        "Essenthos/generation/pages-ottley-2026-10-09/accepted.json",
        "Essenthos/generation/pages-ottley-2026-10-09/places.json",
        "Essenthos/generation/pages-ottley-2026-10-09/transcribed.txt",
    ];

    private static readonly Lazy<IReadOnlyList<CataloguedText>> Known = new(Build);

    public static IReadOnlyList<CataloguedText> All => Known.Value;

    public static CataloguedText Find(string slug) =>
        All.FirstOrDefault(text => string.Equals(text.Slug, slug, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException(
            $"No text \"{slug}\" is declared for conversion. The texts are: {string.Join(", ", All.Select(t => t.Slug))}.");

    /// <summary>A bible4u file read with the repairs its corrections file declares, each checked against the verse the file prints.</summary>
    private static TextSource Bible4u(string resources, string identifier, TextCorrections corrections)
    {
        var bible = new XmlBibleParser().Parse(File.ReadAllText(ResourcePaths.File(resources, "bible4u", $"{identifier}.xml")));
        var slug = Bible4uTextSource.Definitions[identifier].Slug;
        var verses = bible.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse =>
                (Address: (Bible4uTextSource.Canonical(book, slug), chapter.CNumber, verse.VNumber), verse.Text))))
            .ToDictionary(verse => verse.Address, verse => verse.Text);
        corrections.CheckVerses((book, chapter, verse) => verses.GetValueOrDefault((book, chapter, verse)));
        return DeuterocanonTextSource.Extend(
            Bible4uTextSource.Build(bible, Bible4uTextSource.Definitions[identifier], corrections.SourceRepairs()), resources);
    }

    private static IReadOnlyList<CataloguedText> Build()
    {
        CataloguedText Text(string slug, Func<string, TextSource> read, params string[] sources) =>
            new(slug, sources, (resources, _) => read(resources));

        return
        [
            Text(BhsaTextSource.Slug, resources => BhsaTextSource.Build(BhsaProject.Load(Path.Combine(resources, "etcbc"))), "etcbc"),
            Text(NestleTextSource.Slug, resources => NestleTextSource.Read(
                ResourcePaths.File(resources, "Nestle1904", "Nestle1904.xml"),
                ResourcePaths.File(resources, "Nestle1904", "berean-interlinear-glosses.xml")), "Nestle1904"),
            .. ((Edition[])[Edition.Scrivener1894, Edition.Stephanus1550]).Select(edition =>
                Text(TextusReceptusTextSource.Slug(edition),
                    resources => TextusReceptusTextSource.Read(Path.Combine(resources, "TextusReceptus"), edition), "TextusReceptus")),
            Text(TischendorfTextSource.Slug, resources => TischendorfTextSource.Read(Path.Combine(resources, "Tischendorf")), "Tischendorf"),
            Text(WestcottHortTextSource.Slug, resources => WestcottHortTextSource.Read(Path.Combine(resources, "WestcottHort")), "WestcottHort"),
            Text(SblgntTextSource.Slug, SblgntTextSource.Read, "SBLGNT"),
            Text(ByzantineTextSource.Slug, resources => ByzantineTextSource.Read(Path.Combine(resources, "Byzantine")), "Byzantine"),
            Text(SeptuagintTextSource.Slug, resources => SeptuagintTextSource.Read(Path.Combine(resources, "Septuagint")), "Septuagint"),
            Text(SweteTextSource.Slug, resources => SweteTextSource.Read(Path.Combine(resources, "Swete")), "Swete", SwetePages),
            Text(SweteOldGreekTextSource.Slug, resources => SweteOldGreekTextSource.Read(Path.Combine(resources, "Swete")), "Swete"),
            Text(OttleyTextSource.Slug, resources => OttleyTextSource.Read(Path.Combine(resources, "Swete")), ["Swete", .. OttleyPages]),
            Text(AlexandrinusTextSource.Slug, AlexandrinusTextSource.Read, ["Alexandrinus", "Swete", .. OttleyPages]),
            .. CodexTextSource.All.Select(codex => Text(codex.Slug, codex.Read, codex.Folder)),
            Text(SamaritanTextSource.Slug, resources => SamaritanTextSource.Read(Path.Combine(resources, "SamaritanPentateuch")), "SamaritanPentateuch"),
            Text(GeezTextSource.Slug, resources => GeezTextSource.Read(Path.Combine(resources, GeezTextSource.Folder)), GeezTextSource.Folder),
            Text(BereanTextSource.Slug, resources => BereanTextSource.Read(
                ResourcePaths.File(resources, "Berean", "bsb.txt"),
                ResourcePaths.File(resources, "Berean", "bsb_tables.tsv")), "Berean"),
            .. ((string[])["KJV", "RUSV", "UKR"]).Select(identifier => new CataloguedText(
                Bible4uTextSource.Definitions[identifier].Slug,
                [$"bible4u/{identifier}.xml", .. Bible4uExtended],
                (resources, corrections) => Bible4u(resources, identifier, corrections))),
            Text(KulishTextSource.Slug, resources => KulishTextSource.Read(Path.Combine(resources, "Kulish")), "Kulish"),
            .. EbibleTextSource.Definitions.Select(text => Text(text.Value.Slug,
                resources => EbibleTextSource.Read(Path.Combine(resources, text.Key)), text.Key)),
            .. SwordTextSource.Texts.Values.Select(text => Text(text.Definition.Slug,
                resources => SwordTextSource.Read(Path.Combine(resources, text.Folder)), text.Folder)),
            .. EnglishTextSource.Definitions.Select(text => Text(text.Value.Slug,
                resources => DeuterocanonTextSource.Extend(EnglishTextSource.Read(Path.Combine(resources, text.Key)), resources),
                [text.Key, .. Bible4uExtended])),
            .. DeuterocanonTextSource.Texts.Select(text => Text(text.Definition.Slug,
                resources => DeuterocanonTextSource.Read(Path.Combine(resources, text.Folder)), text.Folder)),
            Text(UnfoldingWordTextSource.Slug, resources => UnfoldingWordTextSource.Read(
                Path.Combine(resources, "Door43", UnfoldingWordTextSource.Folder)), $"Door43/{UnfoldingWordTextSource.Folder}"),
            Text(AlmeidaTextSource.Slug, resources => AlmeidaTextSource.Read(Path.Combine(resources, AlmeidaTextSource.Folder)), AlmeidaTextSource.Folder),
            .. Door43TextSource.Definitions.Select(text => Text(text.Value.Slug,
                resources => Door43TextSource.Read(Path.Combine(resources, "Door43", text.Key)), $"Door43/{text.Key}")),
            Text(ClearBible.ClearBibleTextSource.Definition.Slug,
                resources => ClearBible.ClearBibleTextSource.Read(Path.Combine(resources, "ClearBible")), "ClearBible/data/hau"),
        ];
    }
}
