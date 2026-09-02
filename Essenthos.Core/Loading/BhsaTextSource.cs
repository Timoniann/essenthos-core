using System.Text.Json;
using Essenthos.Core.Bhsa;
using Essenthos.Core.Bhsa.Attributes;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// The Biblia Hebraica Stuttgartensia Amstelodamensis as a text. Its books are named in Latin and
/// ordered as the Tanakh orders them, so the order they arrive in is this text's own and the
/// canonical ordinal has to be looked up rather than counted.
/// </summary>
internal static class BhsaTextSource
{
    public const string Slug = "bhsa";

    /// <summary>
    /// CC BY-NC 4.0, read from the ETCBC repository on 2026-08-31. The annotation is the Eep
    /// Talstra Centre's work and the underlying text the German Bible Society's; non-commercial use
    /// is what the licence grants, and anything else needs their consent. Recorded on the text so
    /// that "may we serve this?" is a query rather than a memory.
    /// </summary>
    private static readonly TextDefinition Definition = new(
        Slug: Slug,
        Name: "Biblia Hebraica Stuttgartensia Amstelodamensis",
        NameNative: "תורה נביאים וכתובים",
        Kind: TextKind.CriticalEdition,
        Language: "hbo",
        Direction: TextDirection.RightToLeft,
        Versification: Versification.Original,
        PublishedYear: 2021,
        SourceUrl: "https://github.com/ETCBC/bhsa",
        RightsHolder: "Eep Talstra Centre for Bible and Computer, VU University Amsterdam",
        Licence: "CC-BY-NC-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-nc/4.0/",
        Redistribution: Redistribution.NonCommercialOnly,
        TextualFamily: "Masoretic")
    {
        // Not a courtesy. The ETCBC asks that anything published from BHSA cite the dataset by
        // its DOI, and a licence name and a URL cannot carry that — PRB-0067 is the field that
        // was missing.
        Citation = "Eep Talstra Centre for Bible and Computer, Biblia Hebraica Stuttgartensia " +
                   "Amstelodamensis (BHSA), DANS, https://doi.org/10.17026/dans-z6y-skyh",
    };

    public static TextSource Read(string etcbcPath) => Build(BhsaProject.Load(etcbcPath));

    public static TextSource Build(BhsaProject project)
    {
        var books = new List<BookDraft>(project.Books.Count);
        var position = 0;

        foreach (var book in project.Books)
        {
            position++;
            var canonical = BibleBookAbbreviation.GetAbbreviation(book.Name)
                            ?? throw new InvalidOperationException(
                                $"BHSA names a book \"{book.Name}\" that has no canonical ordinal. Add it to " +
                                $"{nameof(BibleBookAbbreviation)} — until then this text cannot be placed beside " +
                                "any other.");

            books.Add(new BookDraft(
                CanonicalOrdinal: canonical.Ordinal,
                Position: position,
                Name: canonical.FullName.Full,
                Slug: Slugs.Of(canonical.StandardAbbreviation.Full),
                Chapters: book.Chapters
                    .Select(chapter => new ChapterDraft(
                        chapter.Number,
                        chapter.Verses.Select(Draft).ToList()))
                    .ToList(),
                NameNative: book.Name,
                Abbreviation: canonical.StandardAbbreviation.Full));
        }

        return new TextSource(Definition, books);
    }

    private static VerseDraft Draft(Bhsa.Core.Verse verse) =>
        new(verse.Number, verse.Words.Select(Draft).ToList());

    /// <summary>
    /// Thousands of these words have no surface text: Hebrew elides the definite article into the
    /// preposition before it and BHSA still records the article as its own slot, so dropping an
    /// empty word would lose exactly the word a translation's "the" corresponds to.
    /// </summary>
    private static WordDraft Draft(Bhsa.Core.Word word) => new(
        Surface: word.TextUtf8,
        Trailer: word.Trailer,
        Lemma: Empty(word.LexemeUtf8),
        StrongNumber: null,
        Gloss: Empty(word.Gloss),
        Morphology: Morphology(word));

    /// <summary>
    /// BHSA carries features Nestle does not and the Peshitta will carry others again, so the
    /// annotation is json rather than a column per witness. Three kinds of thing live here and are
    /// kept apart by their keys: the word written another way, its own grammar, and the grammar of
    /// the pronominal suffix riding on it.
    ///
    /// The word's language is in here rather than on the text, because the Hebrew Bible has Aramaic
    /// in it: a text has one language and its words do not.
    /// </summary>
    private static string Morphology(Bhsa.Core.Word word)
    {
        var features = new Dictionary<string, string>(20)
        {
            ["language"] = word.Language.Value,
            ["pos"] = word.PartOfSpeech.Value,
        };

        // The same word written other ways. "consonantal" is what a search over unpointed Hebrew has
        // to match, and "phono" with "phonoTrailer" rebuilds a verse in transcription exactly as
        // text and trailer rebuild it in Hebrew — so they are stored as a pair or not at all.
        Add(features, "consonantal", word.ConsonantalUtf8);
        Add(features, "vocalizedLexeme", word.VocalizedLexemeUtf8);
        AddPair(features, "phono", word.PhonologicalTranscription, "phonoTrailer", word.PhonologicalTrailer);
        AddPair(features, "qere", word.Qere, "qereTrailer", word.QereTrailer);

        Add(features, "gender", word.Gender);
        Add(features, "number", word.WordNumberClass);
        Add(features, "person", word.WordPersonClass);
        Add(features, "state", word.NounState);
        Add(features, "stem", word.VerbalStem);
        Add(features, "tense", word.VerbalTense);
        Add(features, "lexicalSet", word.LexicalSet);

        // What this word is inside its phrase, which is not always what it is in the lexicon.
        Add(features, "phrasePos", word.PhraseDependentPartOfSpeech);

        // A suffixed pronoun is another word's grammar carried on this one.
        Add(features, "suffixGender", word.PronominalSuffixGender);
        Add(features, "suffixNumber", word.PronominalWordNumberClass);
        Add(features, "suffixPerson", word.PronominalWordPersonClass);

        if (word.Nametypes.Length > 0)
        {
            features["nameType"] = string.Join(",", word.Nametypes.Select(n => n.Value));
        }

        return JsonSerializer.Serialize(features, MorphologyJson);
    }

    private static readonly JsonSerializerOptions MorphologyJson = new() { WriteIndented = false };

    /// <summary>
    /// BHSA writes "NA" and "unknown" where a feature does not apply to a word. Storing those is
    /// storing the absence of information as information.
    /// </summary>
    private static void Add<T>(Dictionary<string, string> features, string name, StringEnum<T> value)
        where T : StringEnum<T>
    {
        var code = value.Value;
        if (code is "NA" or "unknown" or "none" or "absent")
        {
            return;
        }

        features[name] = code;
    }

    /// <summary>
    /// A reading and its trailer are written together, the trailer included when it is empty. A
    /// third of these words are followed by nothing at all — the article joins straight onto its
    /// noun — and leaving the key out would make an empty trailer and an absent one the same thing,
    /// which is exactly the confusion that cost the corpus its spaces after punctuation.
    /// </summary>
    private static void AddPair(
        Dictionary<string, string> features,
        string name,
        string? value,
        string trailerName,
        string? trailer)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        features[name] = value;
        features[trailerName] = trailer ?? string.Empty;
    }

    private static void Add(Dictionary<string, string> features, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            features[name] = value;
        }
    }

    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
