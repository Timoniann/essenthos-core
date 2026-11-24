using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The texts the corpus holds; the reading of one chapter of one of them is <see cref="ChapterReading"/>.
///
/// The old contract calls a text a corpus and divides them into originals and translations. There
/// is no such division in the model any more — a text's role belongs to its relations — so the
/// mapping here is one-way and lossy on purpose: it exists to keep the old shape answering while
/// the client moves, and it is the one place that still says "original".
/// </summary>
internal static class Texts
{
    private const string OriginalKind = "original";
    private const string TranslationKind = "translation";

    public static string KindOf(TextKind kind) =>
        kind == TextKind.Translation ? TranslationKind : OriginalKind;

    public static CorpusResponse Corpus(Text text, CoverageResponse coverage, bool hasWordMapping) => new(
        text.Slug,
        text.Name,
        KindOf(text.Kind),
        text.Language,
        text.Direction == TextDirection.RightToLeft ? "rtl" : "ltr",
        hasWordMapping,
        text.Licence,
        text.TextualFamily,
        text.Versification.ToString(),
        text.PublishedYear,
        coverage)
    {
        RightsHolder = text.RightsHolder,
        LicenseUrl = text.LicenceUrl,
        Citation = text.Citation,
        SourceUrl = text.SourceUrl,
        Redistribution = EnumSpelling.Of(text.Redistribution),
        Terms = TextRights.Terms(text.Slug, text.Licence, EnumSpelling.Of(text.Redistribution)),
        TermsNote = TextRights.Served(text.Slug, text.Licence, EnumSpelling.Of(text.Redistribution)),
        RightsHolderUrl = TextRights.HolderUrl(text.Slug),
        Translators = text.Translators,
        Editors = text.Editors,
        Edition = text.Edition,
        EditionYear = text.EditionYear,
        About = text.About,
        RightsNote = text.RightsNote,
        Aliases = TextAliases.Of(text.Slug) is { Count: > 0 } aliases ? aliases : null,
        Form = EnumSpelling.Of(text.Kind),
        Script = text.Script,
        PartSources = TextPartSources.Of(text) is { Count: > 0 } parts
            ? [.. parts.Select(p => new PartSourceResponse(p.Name, p.Author, p.Licence, p.LicenceUrl, p.Url, p.Covers))]
            : null,
    };
}
