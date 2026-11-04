using System.Text.Json;
using Essenthos.Core.Berean;
using Essenthos.Core.Bhsa;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Glaux;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Macula;
using Essenthos.Core.MorphGnt;
using Essenthos.Core.Products;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Build;

/// <summary>
/// The served tables of word-level statements made from files: BHSA's syntax (<c>word_group</c> kinds the words do not carry,
/// <c>word_group_word</c>), the second parsings of Nestle 1904 (<c>word_parsing</c>) and the Strong numbers a source proposes
/// beside a word's own (<c>word_strong</c>). A word is the one its text's words product names, by id; the readings are the
/// load steps' own (<see cref="SyntaxLoader"/>, <see cref="MorphGntParsingLoader"/>, <see cref="MaculaAnnotationLoader"/>,
/// <see cref="SeptuagintStrongLoader"/>, <see cref="BereanNumberLoader"/>, <see cref="NewTestamentLinkLoader"/>).
/// </summary>
internal static partial class TableProducts
{
    private const string Bhsa = "BHSA";
    private const string Nestle = "NESTLE1904";
    private const string Brenton = "GRCBRENT";
    private const string Berean = "BSB";
    private const string KingJames = "KJV";
    private const string BereanTables = "Berean/bsb_tables.tsv";
    private const string Zefania = "Zefania/SF_2009-01-20_ENG_KJV_(KJV+).xml";

    private static readonly Table[] Words =
    [
        new("word_group", ["id", "text", "kind", "parent_id", "position", "features", "mother_group_id", "mother_word_id"],
            ["etcbc"], [TextWord.ProductOf(Bhsa), TextBook.ProductOf(Bhsa)], SyntaxGroupRows),
        new("word_group_word", ["word_group_id", "word_id"], ["etcbc"], [TextWord.ProductOf(Bhsa), TextBook.ProductOf(Bhsa)], SyntaxMemberRows),
        new("word_parsing", ["id", "word_id", "lemma", "morphology", "method", "confidence", "source", "note"],
            ["MorphGnt", "Macula"], [TextWord.ProductOf(Nestle)], ParsingRows),
        new("word_strong", ["id", "word_id", "number", "method", "confidence", "source", "note"],
            [StrongHebrew, StrongGreek, BereanTables],
            [TextWord.ProductOf(Brenton), TextWord.ProductOf(Berean), FrameFile.Product(Berean), TextWord.ProductOf(KingJames)],
            WordStrongRows, store => TextConverter.Inputs(store, TextCatalogue.Find(KingJames))),
    ];

    private static (ProductStore Store, List<SyntaxLoader.GroupDraft> Drafts)? _syntax;

    private static List<TextWord> WordsOf(ProductStore store, string slug)
    {
        using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
        return reader.ReadAll();
    }

    private static long IdOf(TextWord word) =>
        word.Id ?? throw new InvalidDataException($"{word.Key} has no id in its words product; convert it again after texts ids.");

    /// <summary>BHSA's syntax drafted as the load drafts it, numbered from 1 in draft order.</summary>
    private static List<SyntaxLoader.GroupDraft> Syntax(ProductStore store)
    {
        if (_syntax is { } held && ReferenceEquals(held.Store, store))
        {
            return held.Drafts;
        }

        Dictionary<int, int> positions;
        using (var reader = store.Open(TextBook.ProductOf(Bhsa), TextBook.Schema))
        {
            positions = reader.Rows().ToDictionary(book => book.Book, book => book.Position);
        }

        var bySlot = new Dictionary<int, long>();
        var slot = 0;
        foreach (var word in WordsOf(store, Bhsa).OrderBy(w => positions[w.Key.Verse.Book]).ThenBy(w => w.Key.Verse.Chapter)
                     .ThenBy(w => w.Key.Verse.Verse).ThenBy(w => w.Key.Position))
        {
            bySlot[++slot] = IdOf(word);
        }

        var drafts = SyntaxLoader.Drafts(BhsaProject.Load(store.FullPath("etcbc")), bySlot);
        for (var i = 0; i < drafts.Count; i++)
        {
            drafts[i].Id = i + 1;
        }

        _syntax = (store, drafts);
        return drafts;
    }

    private static Rows SyntaxGroupRows(ProductStore store) =>
        new(Syntax(store).Select(d => new[]
        {
            Int(d.Id), Bhsa, EnumSpelling.Of(d.Kind), Int(d.Parent?.Id), Int(d.Position), d.Features, Int(d.Mother?.Id), Int(d.MotherWordId),
        }));

    private static Rows SyntaxMemberRows(ProductStore store) =>
        new(Syntax(store).SelectMany(d => d.Words.Select(word => new[] { Int(d.Id), Int(word) })));

    /// <summary>MorphGNT's parsing joined to Nestle 1904 by place and spelling, then MACULA's annotation by position.</summary>
    private static Rows ParsingRows(ProductStore store)
    {
        var nestle = WordsOf(store, Nestle).OrderBy(w => w.Key.Verse.Book).ThenBy(w => w.Key.Verse.Chapter).ThenBy(w => w.Key.Verse.Verse)
            .ThenBy(w => w.Key.Position).ToList();
        var lines = new List<string?[]>(280_000);

        var witness = nestle.Select(w =>
        {
            using var morphology = w.Morphology is null ? null : JsonDocument.Parse(w.Morphology);
            return new JoinWord(w.Key.Verse.Book, w.Key.Verse.Chapter, w.Key.Verse.Verse, w.Surface, MorphGntParsingLoader.Stated(morphology));
        }).ToArray();
        var parsing = MorphGntReader.ReadAll(store.FullPath("MorphGnt"));
        var lexical = EnumSpelling.Of(LinkMethod.Lexical);
        foreach (var row in MorphGntJoin.Of(witness, parsing).Rows)
        {
            var word = parsing[row.Parsing];
            var printed = row.Match == MorphGntMatch.Printed;
            lines.Add([
                Int(IdOf(nestle[row.Witness])), word.Lemma, MorphGntParsingLoader.Morphology(word), lexical,
                Float(printed ? MorphGntParsingLoader.PrintedConfidence : MorphGntParsingLoader.SpellingConfidence), MorphGntParsingLoader.Source,
                printed ? null : MorphGntParsingLoader.Note(witness[row.Witness], word),
            ]);
        }

        var annotated = nestle.Select(w => new MaculaAnnotationLoader.WitnessWord(IdOf(w), w.Key.Verse.Book, w.Key.Verse.Chapter, w.Key.Verse.Verse,
            w.Key.Position, w.Surface)).ToArray();
        var annotation = MaculaReader.ReadFile(store.FullPath("Macula"));
        MaculaAnnotationLoader.Check(annotated, annotation);
        var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
        for (var at = 0; at < annotation.Count; at++)
        {
            var word = annotation[at];
            lines.Add([
                Int(annotated[at].Id), word.Lemma, MaculaAnnotationLoader.Morphology(word), stated, null, MaculaAnnotationLoader.Source,
                word.Surface == annotated[at].Surface ? null : MaculaAnnotationLoader.Note(annotated[at], word),
            ]);
        }

        return new(Numbered(lines));
    }

    private static Rows WordStrongRows(ProductStore store)
    {
        var lines = new List<string?[]>(1_300_000);
        lines.AddRange(SeptuagintProposals(store));
        lines.AddRange(BereanNumbers(store));
        var phrases = KingJamesPhrases(store);
        lines.AddRange(phrases);
        return new(Numbered(lines), $"{phrases.Count} King James phrase numbers");
    }

    /// <summary>Every Greek entry of Strong's that GLAUx's lemma of a Brenton word answers, as the load proposes them.</summary>
    private static IEnumerable<string?[]> SeptuagintProposals(ProductStore store)
    {
        var entries = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in StrongByNumber(store).Where(e => Optional(e.Lemma) is not null && e.StrongNumber.StartsWith('G')))
        {
            var folded = GreekLetters.Bare(entry.Lemma!);
            if (folded.Length == 0)
            {
                continue;
            }

            if (!entries.TryGetValue(folded, out var numbers))
            {
                entries[folded] = numbers = [];
            }

            if (!numbers.Contains(entry.StrongNumber, StringComparer.Ordinal))
            {
                numbers.Add(entry.StrongNumber);
            }
        }

        var lexical = EnumSpelling.Of(LinkMethod.Lexical);
        foreach (var word in WordsOf(store, Brenton).Where(w => w.Lemma is not null))
        {
            var (numbers, bridged) = SeptuagintStrongLoader.Numbers(entries, GreekLetters.Bare(word.Lemma!));
            var confidence = bridged ? SeptuagintStrongLoader.BridgedConfidence : SeptuagintStrongLoader.Shared(Math.Max(1, numbers.Count));
            foreach (var number in numbers)
            {
                yield return [Int(IdOf(word)), number, lexical, Float(confidence), SeptuagintStrongLoader.Source, $"through the lemma {word.Lemma}"];
            }
        }
    }

    /// <summary>The numbers the Berean tables state for the original word a Berean word renders, by canonical address.</summary>
    private static IEnumerable<string?[]> BereanNumbers(ProductStore store)
    {
        var primary = new Dictionary<VerseKey, VerseKey>();
        using (var frame = store.Open(FrameFile.Product(Berean), FrameRow.Schema))
        {
            foreach (var (own, canonical) in frame.Rows().SelectMany(row => row.Verses()))
            {
                primary[own] = canonical[0];
            }
        }

        var english = WordsOf(store, Berean)
            .Select(w => (Word: w, At: primary.TryGetValue(w.Key.Verse, out var moved) ? moved : FrameFile.AtOwn(w.Key.Verse)))
            .GroupBy(w => (w.At.Book, w.At.Chapter, w.At.Verse))
            .ToDictionary(g => g.Key, g => g
                .OrderBy(w => w.Word.Key.Verse.Chapter).ThenBy(w => w.Word.Key.Verse.Verse).ThenBy(w => w.Word.Key.Position)
                .Select(w => new BereanLinkLoader.Word(IdOf(w.Word), 0, w.Word.Surface, w.Word.Strong))
                .ToList());

        var numbered = new HashSet<(long Word, string Number)>(800_000);
        foreach (var (reference, rows) in BereanTable.Verses(store.FullPath(BereanTables)))
        {
            if (!BereanTextSource.Address(reference, out var book, out var chapter, out var number)
                || !english.TryGetValue((book, chapter, number), out var ours)
                || BereanLinkLoader.Claims(rows, ours) is not { } claimed)
            {
                continue;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].StrongNumber.Length == 0)
                {
                    continue;
                }

                foreach (var word in claimed[i] ?? [])
                {
                    numbered.Add((word, rows[i].StrongNumber));
                }
            }
        }

        var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
        return numbered.Select(n => new[] { Int(n.Word), n.Number, stated, null, BereanNumberLoader.Source, null });
    }

    /// <summary>
    /// Every number of a King James word whose Zefania tag names several (one English word over a Greek phrase): the
    /// words product keeps the first; the rest are this table's. The words are the converted text's own, read again so that
    /// a word a correction added (an epistle's subscription) stays out of the alignment, as at conversion.
    /// </summary>
    private static List<string?[]> KingJamesPhrases(ProductStore store)
    {
        var ids = WordsOf(store, KingJames).ToDictionary(w => w.Key, IdOf);
        var text = TextConverter.Read(store, TextCatalogue.Find(KingJames));
        var verses = text.Verses.Where(v => v.Key.VerseLabel.Length == 0).ToDictionary(v => (v.Key.Book, v.Key.Chapter, v.Key.Verse));
        var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
        var rows = new List<string?[]>();
        var seen = new HashSet<(long, string)>();
        foreach (var (address, tags) in NewTestamentLinkLoader.Tagged(store.FullPath(Zefania)))
        {
            if (!verses.TryGetValue(address, out var verse))
            {
                continue;
            }

            var printed = verse.Words.Select((word, at) => (Word: word, Position: at + 1)).Where(w => !w.Word.Added).ToList();
            var spans = TaggedEdition.Align([.. tags.Select(tag => tag.Text)], [.. printed.Select(w => w.Word.Surface)], AmericanSpelling.Fold);
            if (TaggedEdition.Agreement(spans, printed.Count) < NewTestamentLinkLoader.SameVerse)
            {
                continue;
            }

            var projected = NewTestamentLinkLoader.Project(tags, printed.Count, spans);
            for (var i = 0; i < projected.Count; i++)
            {
                if (projected[i].Numbers.Count < 2)
                {
                    continue;
                }

                var id = ids[new WordKey(KingJames, verse.Key, printed[i].Position)];
                foreach (var number in projected[i].Numbers.Where(number => seen.Add((id, number))))
                {
                    rows.Add([Int(id), number, stated, null, NewTestamentLinkLoader.PhraseSource, null]);
                }
            }
        }

        return rows;
    }
}
