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
/// beside a word's own (<c>word_strong</c>). A word is the one its text's words product names, by the id its key gives it
/// (<see cref="WordIdTable"/>); the readings are <see cref="SyntaxGroups"/>, <see cref="MorphGntAnnotation"/>,
/// <see cref="MaculaAnnotation"/>, <see cref="SeptuagintStrongs"/>, <see cref="BereanNumbers"/> and <see cref="CrossWireKingJames"/>.
/// </summary>
internal static partial class TableProducts
{
    private const string Bhsa = "BHSA";
    private const string Nestle = "NESTLE1904";
    private const string Brenton = "GRCBRENT";
    private const string Berean = "BSB";
    private const string KingJames = "KJV";
    private const string BereanTables = "Berean/bsb_tables.tsv";

    private static readonly Table[] Words =
    [
        new("word_group", ["id", "text", "kind", "parent_id", "position", "features", "mother_group_id", "mother_word_id"],
            ["etcbc"], [TextWord.ProductOf(Bhsa), TextBook.ProductOf(Bhsa), WordIdCollision.Product], SyntaxGroupRows),
        new("word_group_word", ["word_group_id", "word_id"], ["etcbc"], [TextWord.ProductOf(Bhsa), TextBook.ProductOf(Bhsa), WordIdCollision.Product],
            SyntaxMemberRows),
        new("word_parsing", ["id", "word_id", "lemma", "morphology", "method", "confidence", "source", "note"],
            ["MorphGnt", "Macula", MaculaHebrewReader.Folder], [TextWord.ProductOf(Nestle), TextWord.ProductOf(Bhsa), WordIdCollision.Product],
            ParsingRows),
        new("word_referent", ["word_id", "referent_word_id", "ordinal", "method", "confidence", "source"],
            ["Macula", MaculaHebrewReader.Folder], [TextWord.ProductOf(Nestle), TextWord.ProductOf(Bhsa), WordIdCollision.Product], ReferentRows),
        new("word_strong", ["id", "word_id", "number", "method", "confidence", "source", "note"],
            [StrongHebrew, StrongGreek, BereanTables],
            [TextWord.ProductOf(Brenton), TextWord.ProductOf(Berean), FrameFile.Product(Berean), TextWord.ProductOf(KingJames), WordIdCollision.Product],
            WordStrongRows, store => TextConverter.Inputs(store, TextCatalogue.Find(KingJames))),
    ];

    private static (ProductStore Store, List<SyntaxGroups.GroupDraft> Drafts)? _syntax;

    private static (ProductStore Store, WordIdTable Ids)? _ids;

    private static (ProductStore Store, (List<MaculaHebrewAnnotation.Parsing> Parsings, List<(long Word, long Referent)> Referents) Annotation)? _hebrew;

    private static List<TextWord> WordsOf(ProductStore store, string slug)
    {
        using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
        return reader.ReadAll();
    }

    private static WordIdTable Ids(ProductStore store)
    {
        if (_ids is { } held && ReferenceEquals(held.Store, store))
        {
            return held.Ids;
        }

        var ids = WordIdTable.For(store);
        _ids = (store, ids);
        return ids;
    }

    /// <summary>BHSA's syntax drafted as the load drafts it, numbered from 1 in draft order.</summary>
    private static List<SyntaxGroups.GroupDraft> Syntax(ProductStore store)
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
            bySlot[++slot] = Ids(store).Of(word.Key);
        }

        var drafts = SyntaxGroups.Drafts(BhsaProject.Load(store.FullPath("etcbc")), bySlot);
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
        var ids = Ids(store);

        var witness = nestle.Select(w =>
        {
            using var morphology = w.Morphology is null ? null : JsonDocument.Parse(w.Morphology);
            return new JoinWord(w.Key.Verse.Book, w.Key.Verse.Chapter, w.Key.Verse.Verse, w.Surface, MorphGntAnnotation.Stated(morphology));
        }).ToArray();
        var parsing = MorphGntReader.ReadAll(store.FullPath("MorphGnt"));
        var lexical = EnumSpelling.Of(LinkMethod.Lexical);
        foreach (var row in MorphGntJoin.Of(witness, parsing).Rows)
        {
            var word = parsing[row.Parsing];
            var printed = row.Match == MorphGntMatch.Printed;
            lines.Add([
                Int(ids.Of(nestle[row.Witness].Key)), word.Lemma, MorphGntAnnotation.Morphology(word), lexical,
                Float(printed ? MorphGntAnnotation.PrintedConfidence : MorphGntAnnotation.SpellingConfidence), MorphGntAnnotation.Source,
                printed ? null : MorphGntAnnotation.Note(witness[row.Witness], word),
            ]);
        }

        var (annotated, annotation) = Macula(store, nestle);
        var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
        for (var at = 0; at < annotation.Count; at++)
        {
            var word = annotation[at];
            lines.Add([
                Int(annotated[at].Id), word.Lemma, MaculaAnnotation.Morphology(word), stated, null, MaculaAnnotation.Source,
                word.Surface == annotated[at].Surface ? null : MaculaAnnotation.Note(annotated[at], word),
            ]);
        }

        var hebrew = Hebrew(store).Parsings;
        lines.AddRange(hebrew.Select(p => new[] { Int(p.Word), p.Lemma, p.Morphology, stated, null, MaculaHebrewAnnotation.Source, p.Note }));
        return new(Numbered(lines), $"{hebrew.Count} BHSA words parsed by MACULA Hebrew");
    }

    /// <summary>MACULA Hebrew's morphs placed on BHSA's words by their letters (<see cref="MaculaHebrewJoin"/>), read once per store.</summary>
    private static (List<MaculaHebrewAnnotation.Parsing> Parsings, List<(long Word, long Referent)> Referents) Hebrew(ProductStore store)
    {
        if (_hebrew is { } held && ReferenceEquals(held.Store, store))
        {
            return held.Annotation;
        }

        var ids = Ids(store);
        var words = WordsOf(store, Bhsa)
            .Select(w => new MaculaHebrewJoin.Word(ids.Of(w.Key), w.Key.Verse.Book, w.Key.Verse.Chapter, w.Key.Verse.Verse, w.Key.Position, w.Surface))
            .ToList();
        var annotation = MaculaHebrewAnnotation.Of(MaculaHebrewReader.ReadFile(store.FullPath(MaculaHebrewReader.Folder)), words);
        _hebrew = (store, annotation);
        return annotation;
    }

    /// <summary>Nestle 1904's words beside MACULA's, checked to be the same words in the same order.</summary>
    private static (MaculaAnnotation.WitnessWord[] Annotated, IReadOnlyList<MaculaWord> Annotation) Macula(ProductStore store, List<TextWord> nestle)
    {
        var ids = Ids(store);
        var annotated = nestle.Select(w => new MaculaAnnotation.WitnessWord(ids.Of(w.Key), w.Key.Verse.Book, w.Key.Verse.Chapter, w.Key.Verse.Verse,
            w.Key.Position, w.Surface)).ToArray();
        var annotation = MaculaReader.ReadFile(store.FullPath("Macula"));
        MaculaAnnotation.Check(annotated, annotation);
        return (annotated, annotation);
    }

    /// <summary>MACULA's word each Nestle word refers back to, a row per word referred to (<see cref="MaculaAnnotation.Referents"/>).</summary>
    private static Rows ReferentRows(ProductStore store)
    {
        var nestle = WordsOf(store, Nestle).OrderBy(w => w.Key.Verse.Book).ThenBy(w => w.Key.Verse.Chapter).ThenBy(w => w.Key.Verse.Verse)
            .ThenBy(w => w.Key.Position).ToList();
        var (annotated, annotation) = Macula(store, nestle);
        var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
        // A word's referents come together, in the order MACULA lists them; the ordinal is the place among them.
        var lines = new List<string?[]>();
        var (word, ordinal) = (-1, 0);
        foreach (var pair in MaculaAnnotation.Referents(annotation))
        {
            (word, ordinal) = (pair.Word, pair.Word == word ? ordinal + 1 : 0);
            lines.Add([Int(annotated[pair.Word].Id), Int(annotated[pair.Referent].Id), Int(ordinal), stated, null, MaculaAnnotation.Source]);
        }

        var greek = lines.Select(l => l[0]).Distinct().Count();
        var hebrew = Hebrew(store).Referents;
        var (hebrewWord, hebrewOrdinal) = (-1L, 0);
        foreach (var pair in hebrew)
        {
            (hebrewWord, hebrewOrdinal) = (pair.Word, pair.Word == hebrewWord ? hebrewOrdinal + 1 : 0);
            lines.Add([Int(pair.Word), Int(pair.Referent), Int(hebrewOrdinal), stated, null, MaculaHebrewAnnotation.Source]);
        }

        return new(lines, $"{greek} Nestle words and {hebrew.Select(p => p.Word).Distinct().Count()} BHSA words refer to another");
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
        var ids = Ids(store);
        foreach (var word in WordsOf(store, Brenton).Where(w => w.Lemma is not null))
        {
            var (numbers, bridged) = SeptuagintStrongs.Numbers(entries, GreekLetters.Bare(word.Lemma!));
            var confidence = bridged ? SeptuagintStrongs.BridgedConfidence : SeptuagintStrongs.Shared(Math.Max(1, numbers.Count));
            foreach (var number in numbers)
            {
                yield return [Int(ids.Of(word.Key)), number, lexical, Float(confidence), SeptuagintStrongs.Source, $"through the lemma {word.Lemma}"];
            }
        }
    }

    private const string BereanNumberSource =
        "Berean Standard Bible translation tables (Str Heb, Str Grk), bereanbible.com, public domain";

    /// <summary>
    /// The numbers the Berean tables state for the original word a Berean word renders, by canonical address. A number is
    /// the file's statement about its own English, independent of how BHSA or Nestle number the word, so it lives in
    /// <c>word_strong</c> under the tables' name, and every word of a phrase carries its row's number: the tables number
    /// the phrase, and saying which of its words the number belongs to would be ours. A verse whose phrases part from
    /// ours gets nothing.
    /// </summary>
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

        var ids = Ids(store);
        var english = WordsOf(store, Berean)
            .Select(w => (Word: w, At: primary.TryGetValue(w.Key.Verse, out var moved) ? moved : FrameFile.AtOwn(w.Key.Verse)))
            .GroupBy(w => (w.At.Book, w.At.Chapter, w.At.Verse))
            .ToDictionary(g => g.Key, g => g
                .OrderBy(w => w.Word.Key.Verse.Chapter).ThenBy(w => w.Word.Key.Verse.Verse).ThenBy(w => w.Word.Key.Position)
                .Select(w => new BereanLinks.Word(ids.Of(w.Word.Key), 0, w.Word.Surface, w.Word.Strong))
                .ToList());

        var numbered = new HashSet<(long Word, string Number)>(800_000);
        foreach (var (reference, rows) in BereanTable.Verses(store.FullPath(BereanTables)))
        {
            if (!BereanTextSource.Address(reference, out var book, out var chapter, out var number)
                || !english.TryGetValue((book, chapter, number), out var ours)
                || BereanLinks.Claims(rows, ours) is not { } claimed)
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
        return numbered.Select(n => new[] { Int(n.Word), n.Number, stated, null, BereanNumberSource, null });
    }

    /// <summary>
    /// Every number of a King James word whose CrossWire element names several (an English word over a Greek phrase,
    /// <em>God</em> over ὁ θεός): the words product keeps the word's own number (<see cref="CrossWireKingJames.Own"/>); the
    /// rest are this table's. A word with no number of its own (<em>the</em> over an element naming no article) gets none
    /// here either: a reader takes a word's only number in this table as the number it renders. Nor does a function word
    /// (<see cref="CrossWireKingJames.Closed"/>), which renders the one Greek word of its class or nothing. The words are
    /// the converted text's own, read again so that a word a correction added (an epistle's subscription) stays out of the
    /// pairing, as at conversion.
    /// </summary>
    private static List<string?[]> KingJamesPhrases(ProductStore store)
    {
        var table = Ids(store);
        var ids = WordsOf(store, KingJames).ToDictionary(w => w.Key, w => table.Of(w.Key));
        var text = TextConverter.Read(store, TextCatalogue.Find(KingJames));
        var keys = new List<WordKey>(190_000);
        var corpus = new List<CorpusVerse>(8_000);
        foreach (var verse in text.Verses.Where(v => v.Key.VerseLabel.Length == 0 && CrossWireKingJames.InNewTestament(v.Key.Book)))
        {
            var printed = new List<CorpusWord>(verse.Words.Count);
            for (var at = 0; at < verse.Words.Count; at++)
            {
                if (!verse.Words[at].Added)
                {
                    printed.Add(new CorpusWord(keys.Count, verse.Words[at].Surface));
                    keys.Add(new WordKey(KingJames, verse.Key, at + 1));
                }
            }

            corpus.Add(new CorpusVerse(verse.Key.Book, verse.Key.Chapter, verse.Key.Verse, [], printed));
        }

        var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
        var rows = new List<string?[]>();
        foreach (var (word, tag) in CrossWireKingJames.Lay(store.FullPath(CrossWireKingJames.Folder), corpus).Tags.OrderBy(tag => tag.Key))
        {
            if (tag.Number is null || tag.Closed)
            {
                continue;
            }

            var id = ids[keys[(int)word]];
            foreach (var number in tag.Numbers.Distinct(StringComparer.Ordinal).Where(number => number != tag.Number))
            {
                rows.Add([Int(id), number, stated, null, Sources.KingJamesPhraseCredit, null]);
            }
        }

        return rows;
    }
}
