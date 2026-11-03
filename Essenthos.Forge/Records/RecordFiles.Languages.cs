using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

internal static partial class RecordFiles
{
    /// <summary>
    /// The languages of the files. A language with no rows in a table gets an empty file, so adding a
    /// language is adding its folder.
    /// </summary>
    private static string[] Languages => ["eng", "ukr", "rus", "deu", "spa"];

    private static string[] Renderings => ["ukr", "rus", "deu", "spa"];

    private static IEnumerable<RecordFile> PerLanguage() =>
    [
        .. Renderings.Select(language => new RecordFile($"records/i18n/{language}/lines", ProductKind.Hand,
            "slug line english by",
            $"""
            SELECT e.slug, d.text, d.english, d.source FROM entity_distinguisher d JOIN entity e ON e.id = d.entity_id
            WHERE d.language = '{language}' ORDER BY 1
            """,
            $"""
            INSERT INTO entity_distinguisher (entity_id, language, text, english, source)
            SELECT e.id, '{language}', s.line, s.english, s.by FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug
            """,
            language == Renderings[0] ? ["entity_distinguisher"] : [],
            About: "A record's line under the name in this language, and the English line it renders.")),

        .. Renderings.Select(language => new RecordFile($"records/i18n/{language}/notes", ProductKind.Hand,
            "slug notes english_sha256 by",
            $"""
            SELECT e.slug, n.text, n.english_sha256, n.source FROM entity_note_translation n JOIN entity e ON e.id = n.entity_id
            WHERE n.language = '{language}' ORDER BY 1
            """,
            $"""
            INSERT INTO entity_note_translation (entity_id, language, text, english_sha256, source)
            SELECT e.id, '{language}', s.notes, s.english_sha256, s.by FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug
            """,
            language == Renderings[0] ? ["entity_note_translation"] : [],
            About: "A record's notes in this language; english_sha256 is the English they render, so a changed English shows them stale.")),

        .. Languages.Select(language => new RecordFile($"records/i18n/{language}/name-forms", ProductKind.Hand,
            "slug grammatical_case form method confidence? by run?",
            $"""
            SELECT e.slug, f.grammatical_case, f.form, f.method, f.confidence::text, f.source, f.run
            FROM entity_name_form f JOIN entity e ON e.id = f.entity_id WHERE f.language = '{language}' ORDER BY 1, 2
            """,
            $"""
            INSERT INTO entity_name_form (entity_id, language, grammatical_case, form, method, confidence, source, run)
            SELECT e.id, '{language}', s.grammatical_case, s.form, s.method, s.confidence::double precision, s.by, s.run
            FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug, s.grammatical_case
            """,
            language == Languages[0] ? ["entity_name_form"] : [],
            About: "A record's name in each grammatical case of this language.")),

        .. Renderings.Select(language => new RecordFile($"records/i18n/{language}/glosses", ProductKind.Hand,
            "english text by",
            $"SELECT english, text, source FROM entity_name_translation WHERE language = '{language}' ORDER BY 1, 2",
            $"""
            INSERT INTO entity_name_translation (language, english, text, source)
            SELECT '{language}', english, text, by FROM s ORDER BY english, text
            """,
            language == Renderings[0] ? ["entity_name_translation"] : [],
            About: "The kinds and meanings of names, rendered from their English.")),

        .. Renderings.Select(language => new RecordFile($"records/i18n/{language}/texts", ProductKind.Hand,
            "english_sha256 text by",
            $"SELECT english_sha256, text, source FROM text_translation WHERE language = '{language}' ORDER BY 1, 2",
            $"""
            INSERT INTO text_translation (language, english_sha256, text, source)
            SELECT '{language}', english_sha256, text, by FROM s ORDER BY english_sha256, text
            """,
            language == Renderings[0] ? ["text_translation"] : [],
            About: "Renderings of English the records and the datasets give (lines, places today, reasons, event names), keyed by the English's SHA-256.")),
    ];
}
