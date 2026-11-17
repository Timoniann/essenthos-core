using System.Globalization;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The words of a chapter as the reader is given them, with everything the corpus says of each word, and what each text
/// prints and numbers around them. The API answers the reader with it and the Forge's pane producer writes the same
/// reading ahead of time, so the two cannot drift apart.
/// </summary>
internal static class ChapterReading
{
    /// <summary>The group kinds that are an edition's statement about its own words rather than an analysis of them.</summary>
    private static readonly WordGroupKind[] EditionMarks =
    [
        WordGroupKind.Supplied, WordGroupKind.Restored, WordGroupKind.Doubtful, WordGroupKind.Subscription,
        WordGroupKind.Corrupt, WordGroupKind.NotInEthiopic, WordGroupKind.NotInGreek,
    ];

    /// <summary>
    /// A word of BHSA carries these under its own names; the old contract has a field for each.
    /// Everything else the annotation holds stays in the database and is reached by the new shapes.
    /// </summary>
    private const string PartOfSpeech = "pos";
    private const string Language = "language";
    private const string Phono = "phono";
    private const string PhonoTrailer = "phonoTrailer";

    /// <summary>The language whose words the Greek lexicon glosses.</summary>
    private const string Greek = "grc";

    /// <summary>The language Dillmann's lexicon glosses, and whose words are given the aligned Greek's meaning.</summary>
    private const string Geez = "gez";

    /// <summary>How many of Dillmann's glosses and Greek equivalents a word carries; his articles run to dozens.</summary>
    private const int LatinShown = 5;

    private const int GreekShown = 4;

    /// <summary>A Greek equivalent longer than this is a phrase quoted from a verse, not a gloss.</summary>
    private const int GreekShownWords = 3;

    /// <summary>How a gloss was reached, as <see cref="LexiconGlossResponse.Via"/> spells it.</summary>
    private const string ByStatedNumber = "strong";
    private const string ByLemma = "lemma";
    private const string ByEqualWord = "equals";
    private const string ByProposedNumber = "strong-candidate";
    private const string BySpelling = "form";

    /// <summary>
    /// Reads one chapter as this text numbers it. The words and the edition's notes each come back
    /// in one query: a chapter is a thousand words, and a query per word is a thousand round trips.
    /// A note-only verse stays in the response too — it is how an edition says a verse is absent
    /// from its text, and the note is the only thing the reader has to show at that address.
    /// </summary>
    public static async Task<IList<TextVerseResponse>> ReadChapter(
        AppDbContext db,
        int textId,
        int bookOrdinal,
        int chapter,
        CancellationToken cancellationToken)
    {
        var verses = await db.Verses
            .Where(verse => verse.TextId == textId
                            && verse.Book!.CanonicalOrdinal == bookOrdinal
                            && verse.ChapterNumber == chapter)
            .OrderBy(verse => verse.Sequence).ThenBy(verse => verse.Number).ThenBy(verse => verse.Label)
            .Select(verse => new VerseRow(verse.Number, verse.Label))
            .ToListAsync(cancellationToken);

        var rows = await db.Words
            .Where(word => word.TextId == textId
                           && word.Verse!.Book!.CanonicalOrdinal == bookOrdinal
                           && word.Verse.ChapterNumber == chapter)
            .OrderBy(w => w.Verse!.Number).ThenBy(w => w.Verse!.Label).ThenBy(w => w.Position)
            .Select(w => new WordRow(
                w.Verse!.Number, w.Verse!.Label, w.Id, w.Surface, w.Trailer, w.Gloss, w.Lemma, w.StrongNumber,
                w.Morphology, w.Elided, w.Break, w.NormalisedText, w.VerseId))
            .ToListAsync(cancellationToken);

        var counterparts = await Counterparts(db, rows.Select(r => (r.Id, r.VerseId, r.Text)), cancellationToken);
        counterparts = counterparts with
        {
            Glossed = await Glossed(
                db, textId,
                [.. rows.Select(r => new GlossWanted(r.Id, r.Text, r.NormalisedText, r.Gloss, r.Lemma, r.StrongNumber, r.VerseId))],
                counterparts.Proposed, cancellationToken),
        };
        counterparts = await Meant(db, textId, [.. rows.Select(r => (r.Id, r.VerseId, r.Text))], counterparts, cancellationToken);
        var notes = await db.VerseNotes
            .Where(note => note.Verse!.TextId == textId
                           && note.Verse.Book!.CanonicalOrdinal == bookOrdinal
                           && note.Verse.ChapterNumber == chapter)
            .OrderBy(note => note.Verse!.Number).ThenBy(note => note.Verse!.Label).ThenBy(note => note.Position)
            .Select(note => new NoteRow(
                note.Verse!.Number, note.Verse.Label, note.Kind, note.Content, note.AnchorWordId,
                note.AnchorWord == null ? null : note.AnchorWord.Surface))
            .ToListAsync(cancellationToken);

        return Group(verses, rows, counterparts, notes);
    }

    /// <summary>
    /// The notes each of these texts printed on this book's title, by text, for a text that printed
    /// any. They come with the book's first chapter, where the title stands on the page, and with no
    /// other chapter.
    /// </summary>
    public static async Task<Dictionary<int, BookNotesResponse>> BookNotes(
        AppDbContext db,
        IReadOnlyCollection<int> textIds,
        int bookOrdinal,
        int chapter,
        CancellationToken cancellationToken)
    {
        if (chapter != 1)
        {
            return [];
        }

        var notes = await db.BookNotes
            .Where(note => textIds.Contains(note.Book!.TextId) && note.Book.CanonicalOrdinal == bookOrdinal)
            .OrderBy(note => note.Position)
            .Select(note => new { note.Book!.TextId, note.Book.NameNative, note.Kind, note.Content })
            .ToListAsync(cancellationToken);

        return notes
            .GroupBy(note => note.TextId)
            .ToDictionary(
                group => group.Key,
                group => new BookNotesResponse(
                    group.First().NameNative,
                    [.. group.Select(note => new SourceNoteResponse(EnumSpelling.Of(note.Kind), note.Content))]));
    }

    /// <summary>
    /// The notes a text printed on the headings of its chapters, by the canonical address of the
    /// verse each chapter opens with, for the chapters whose first verse stands in this canonical
    /// chapter. A psalm the edition numbers otherwise brings its notes to the canonical psalm it opens.
    /// </summary>
    public static async Task<Dictionary<int, List<ChapterNotesResponse>>> ChapterNotesByCanonicalVerse(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var notes = await db.ChapterNotes
            .Where(note => note.Chapter!.TextId == textId && note.Chapter.Book!.CanonicalOrdinal == canonicalBook)
            .OrderBy(note => note.Position)
            .Select(note => new { note.ChapterId, note.Heading, note.Kind, note.Content })
            .ToListAsync(cancellationToken);
        if (notes.Count == 0)
        {
            return [];
        }

        var chapterIds = notes.Select(note => note.ChapterId).Distinct().ToList();
        var opening = (await db.VerseReferences
                .Where(reference => reference.IsPrimary && chapterIds.Contains(reference.Verse!.ChapterId))
                .Select(reference => new
                {
                    reference.Verse!.ChapterId,
                    reference.Verse.Sequence,
                    reference.Verse.Number,
                    reference.Verse.Label,
                    reference.CanonicalBook,
                    reference.CanonicalChapter,
                    reference.CanonicalVerse,
                })
                .ToListAsync(cancellationToken))
            .GroupBy(verse => verse.ChapterId)
            .Select(verses => verses
                .OrderBy(verse => verse.Sequence).ThenBy(verse => verse.Number)
                .ThenBy(verse => verse.Label, StringComparer.Ordinal)
                .First())
            .Where(verse => verse.CanonicalBook == canonicalBook && verse.CanonicalChapter == canonicalChapter)
            .ToDictionary(verse => verse.ChapterId, verse => verse.CanonicalVerse);

        return Grouped(notes
            .Where(note => opening.ContainsKey(note.ChapterId))
            .Select(note => (opening[note.ChapterId], note.Heading, note.Kind, note.Content)));
    }

    /// <summary>
    /// The notes a text printed on the heading of this chapter of its own, by the number of the verse
    /// the chapter opens with.
    /// </summary>
    public static async Task<Dictionary<int, List<ChapterNotesResponse>>> ChapterNotesOfOwnChapter(
        AppDbContext db,
        int textId,
        int bookOrdinal,
        int chapter,
        CancellationToken cancellationToken)
    {
        var notes = await db.ChapterNotes
            .Where(note => note.Chapter!.TextId == textId && note.Chapter.Book!.CanonicalOrdinal == bookOrdinal
                                                           && note.Chapter.Number == chapter)
            .OrderBy(note => note.Position)
            .Select(note => new { note.ChapterId, note.Heading, note.Kind, note.Content })
            .ToListAsync(cancellationToken);
        if (notes.Count == 0)
        {
            return [];
        }

        var first = await db.Verses
            .Where(verse => verse.ChapterId == notes[0].ChapterId)
            .OrderBy(verse => verse.Sequence).ThenBy(verse => verse.Number).ThenBy(verse => verse.Label)
            .Select(verse => verse.Number)
            .FirstAsync(cancellationToken);

        return Grouped(notes.Select(note => (first, note.Heading, note.Kind, note.Content)));
    }

    private static Dictionary<int, List<ChapterNotesResponse>> Grouped(
        IEnumerable<(int Verse, string Heading, VerseNoteKind Kind, string Content)> notes) =>
        notes
            .GroupBy(note => note.Verse)
            .ToDictionary(
                byVerse => byVerse.Key,
                byVerse => byVerse
                    .GroupBy(note => note.Heading)
                    .Select(byHeading => new ChapterNotesResponse(
                        byVerse.Key,
                        byHeading.Key,
                        [.. byHeading.Select(note => new SourceNoteResponse(EnumSpelling.Of(note.Kind), note.Content))]))
                    .ToList());

    /// <summary>
    /// The words of the ancient witnesses each of these words reaches, in one query rather than one
    /// per word.
    ///
    /// The client treats this set as opaque and highlights where two words' sets intersect, so what
    /// the set holds decides what can light up together. Holding link ids would only ever join two
    /// texts that are **directly** linked — the Ukrainian and the Synodal each link to BHSA and
    /// never to each other, so hovering one would leave the other dark although both name the same
    /// Hebrew word.
    ///
    /// Holding the witness's word ids instead makes the join happen through the witness, which is
    /// what the whole model is shaped for: five texts meet at the word they all render, and a sixth
    /// joins them by being linked to that same word rather than to any of them. A word of a witness
    /// carries its own id, so it meets the translations that reach it.
    /// </summary>
    private static async Task<Reached> Counterparts(
        AppDbContext db,
        IEnumerable<(long Id, int Verse, string Text)> words,
        CancellationToken cancellationToken)
    {
        var forms = new Dictionary<long, string>();
        var verses = new HashSet<int>();
        foreach (var (id, verse, text) in words)
        {
            forms[id] = text.ToLowerInvariant();
            verses.Add(verse);
        }

        var ids = forms.Keys.ToList();

        // The words are every word of their verses, so the verses name the same rows, and a chapter's verses are one range
        // of a verse-keyed index where its words' hashed ids are as many lookups scattered over a word-keyed one.
        var inVerses = verses.ToList();
        var own = await db.Words
            .Where(w => inVerses.Contains(w.VerseId) && w.Text!.Kind != TextKind.Translation)
            .Select(w => new { WordId = w.Id, Reached = w.Id })
            .ToListAsync(cancellationToken);

        // The other side's text is its verse's: the row carries the verse, so no word of the other side is read for it.
        var reaching = await (
                from side in db.LinkWords
                where inVerses.Contains(side.VerseId)
                join other in db.LinkWords on side.LinkId equals other.LinkId
                where other.Side != side.Side
                join verse in db.Verses on other.VerseId equals verse.Id
                where verse.Text!.Kind != TextKind.Translation
                select new { side.WordId, side.LinkId, Reached = other.WordId })
            .ToListAsync(cancellationToken);
        var reached = reaching.Select(row => new { row.WordId, row.Reached }).ToList();
        var witnessed = reaching.Select(row => (row.WordId, row.LinkId)).ToHashSet();

        // Only the links that reach a witness, because those are the ones the highlighting is built
        // from. A translation is now linked to other translations too — the Synodal to the King
        // James, which is how it reaches the Hebrew at all — and letting one of those describe the
        // word would report the confidence of a step the reader never sees. Whether a link reaches
        // one is what the read above found.
        var evidence = (await db.LinkWords
                .Where(side => inVerses.Contains(side.VerseId))
                .Select(side => new { side.WordId, side.LinkId, side.Link!.Method, side.Link!.Confidence, side.Link.Relation })
                .ToListAsync(cancellationToken))
            .Where(row => row.Relation == LinkRelation.Expands || witnessed.Contains((row.WordId, row.LinkId)))
            .ToList();

        // Two links equally sure are told apart by the method the merge ranks higher, then by the link's id, which is derived
        // from its words: the database's row order would let the same links describe a word differently from load to load.
        evidence = [.. evidence
            .OrderByDescending(row => row.Confidence is null)
            .ThenByDescending(row => row.Confidence)
            .ThenByDescending(row => LinkMethodPrecedence.Of(row.Method))
            .ThenBy(row => row.LinkId)];

        var strongest = evidence
            .GroupBy(row => row.WordId)
            .ToDictionary(group => group.Key, group => group.Select(row => Provenance(row.Method, row.Confidence)).First());

        // A word whose absence is stated is not a word nothing was found for, and a reader has to
        // be able to tell them apart — that difference is what the schema was built to hold and
        // what 22,155 King James italics say outright.
        var absent = evidence
            .Where(row => row.Relation is LinkRelation.Expands or LinkRelation.Omits)
            .GroupBy(row => row.WordId)
            .ToDictionary(group => group.Key, group => EnumSpelling.Of(group.First().Relation));

        // Asked of the group table rather than tested per word: a chapter is a thousand words, and
        // BHSA's are each in seven groups, so an EXISTS per word would walk seven rows a thousand
        // times to answer no.
        var marked = await db.WordGroupWords
            .Where(m => ids.Contains(m.WordId) && EditionMarks.Contains(m.WordGroup!.Kind))
            .Select(m => new
            {
                m.WordId,
                m.WordGroup!.Kind,
                Letters = m.WordGroup.Features == null ? null : m.WordGroup.Features.RootElement.GetProperty("letters").GetString(),
            })
            .ToListAsync(cancellationToken);

        // A word that goes with another renders through its own link, which is already among the
        // witnesses above; what it adds is the word it goes with, so the reader can name it.
        var attached = await db.LinkWords
            .Where(side => inVerses.Contains(side.VerseId) && side.Role != null && side.HeadWordId != null)
            .Select(side => new { side.WordId, side.Role, HeadId = side.HeadWordId!.Value, Head = side.HeadWord!.Surface })
            .ToListAsync(cancellationToken);

        var roles = await db.OccupationWords
            .Where(w => ids.Contains(w.WordId))
            .Select(w => new
            {
                w.WordId,
                Occupation = w.PersonOccupation!.Occupation!.Slug,
                w.PersonOccupation.Occupation.Category,
                Person = w.PersonOccupation.Person!.Slug,
            })
            .ToListAsync(cancellationToken);

        var witnesses = own.Concat(reached).ToLookup(row => row.WordId, row => row.Reached);
        return new Reached(
            witnesses,
            strongest,
            absent,
            marked
                .GroupBy(m => m.WordId)
                .ToDictionary(group => group.Key, group => group.Select(m => m.Kind).Distinct().Order().ToArray()),
            await Annotations.AllOf(db, ids, cancellationToken),
            await Proposed(db, ids, cancellationToken))
        {
            Bracketed = marked
                .Where(m => m.Letters != null)
                .GroupBy(m => m.WordId)
                .ToDictionary(group => group.Key, group => group.First().Letters!),
            Attached = attached
                .GroupBy(row => row.WordId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(row => row.Role)
                        .ThenBy(row => row.HeadId)
                        .Select(row => new AttachedResponse(EnumSpelling.Of(row.Role!.Value), row.HeadId, row.Head))
                        .First()),
            Roles = roles
                .GroupBy(row => row.WordId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<WordRoleResponse>)[.. group
                        .OrderBy(row => row.Person, StringComparer.Ordinal).ThenBy(row => row.Occupation, StringComparer.Ordinal)
                        .Select(row => new WordRoleResponse(row.Occupation, row.Category, row.Person))]),
            Referents = await Referents(
                db, witnesses, forms, attached.Where(row => row.Role != null).ToLookup(row => row.WordId, row => row.HeadId), cancellationToken),
        };
    }

    /// <summary>How many words a chain of references back is followed through before it is given up.</summary>
    private const int ReferenceSteps = 8;

    /// <summary>The source's classes of word a chain of references is followed through: a pronoun, an article standing for one.</summary>
    private static readonly HashSet<string> Referring = new(StringComparer.Ordinal) { "pron", "det" };

    /// <summary>
    /// The records each word refers to: through every witness word it is or reaches that refers back to another word
    /// (<see cref="WordReferent"/>), the record that word names. A word referred to that names nobody and is itself a pronoun
    /// (by the same source's class of it) is followed to its own reference, as far as <see cref="ReferenceSteps"/>.
    /// <para>
    /// All or nothing: where any word referred to ends nowhere (a noun naming nobody, <em>the council</em>, or only a title),
    /// the word is given no record at all, since <em>us</em> shown as Paul alone, when the source says Paul and the Romans,
    /// is wrong. A noun the source gives a reference to is not a word standing for a person and is given none. Several
    /// records where the source refers to several words that all name one. A word naming a title and its bearer (Χριστοῦ)
    /// gives the bearer. It is not a name: the word does not name the record, it stands for a word that does.
    /// </para>
    /// </summary>
    private static async Task<Dictionary<long, IReadOnlyList<ReferentResponse>>> Referents(
        AppDbContext db,
        ILookup<long, long> witnesses,
        IReadOnlyDictionary<long, string> forms,
        ILookup<long, long> heads,
        CancellationToken cancellationToken)
    {
        var direct = new Dictionary<long, List<(long Referent, string Source)>>();
        var asked = new HashSet<long>();
        var frontier = witnesses.SelectMany(group => group).Distinct().ToList();
        for (var step = 0; step < ReferenceSteps && frontier.Count > 0; step++)
        {
            asked.UnionWith(frontier);
            var rows = await db.WordReferents
                .Where(r => frontier.Contains(r.WordId))
                .OrderBy(r => r.Ordinal).ThenBy(r => r.Source).ThenBy(r => r.ReferentWordId)
                .Select(r => new { r.WordId, r.ReferentWordId, r.Source })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                (direct.TryGetValue(row.WordId, out var list) ? list : direct[row.WordId] = []).Add((row.ReferentWordId, row.Source));
            }

            frontier = [.. rows.Select(r => r.ReferentWordId).Where(id => !asked.Contains(id)).Distinct()];
        }

        if (direct.Count == 0)
        {
            return [];
        }

        var referred = direct.Values.SelectMany(list => list.Select(r => r.Referent)).Distinct().ToList();
        var named = await Annotations.AllOf(db, referred, cancellationToken);
        var referredWords = await db.Words
            .Where(w => referred.Contains(w.Id))
            .Select(w => new { w.Id, w.Surface, w.Morphology })
            .ToListAsync(cancellationToken);
        var surfaces = referredWords.ToDictionary(w => w.Id, w => w.Surface);
        var members = referredWords.Where(w => OneOfAPeople(w.Morphology)).Select(w => w.Id).ToHashSet();
        var sources = direct.Values.SelectMany(list => list.Select(r => r.Source)).Distinct().ToList();
        var classed = direct.Keys.Concat(referred).Concat(witnesses.SelectMany(group => group)).Distinct().ToList();
        var parsings = await db.WordParsings
            .Where(p => classed.Contains(p.WordId) && sources.Contains(p.Source))
            .Select(p => new { p.WordId, p.Morphology, p.Lemma })
            .ToListAsync(cancellationToken);
        var classes = new Dictionary<long, string>();
        var kinds = new Dictionary<long, string>();
        var suffixes = new Dictionary<long, (string Agreement, string? Lemma)>();
        var bySuffix = new HashSet<long>();
        var languages = new Dictionary<long, string>();
        var agreements = new Dictionary<long, Agreement>();
        foreach (var parsing in parsings)
        {
            var root = parsing.Morphology.RootElement;
            // A pronoun MACULA Hebrew parses no further is asked of BHSA's parsing (Contradicting); any other word is as parsed here.
            if (Agreement.Of(root) is var says
                && (says != default || !(root.TryGetProperty(PartOfSpeech, out var part) && part.GetString() == "pronoun")))
            {
                agreements[parsing.WordId] = says;
            }

            if (root.TryGetProperty("class", out var value) && value.GetString() is { } kind)
            {
                // MACULA Hebrew's demonstratives are adjectives by class and pronouns by part of speech.
                kind = root.TryGetProperty(PartOfSpeech, out var pos) && pos.GetString() == "pronoun" ? Pronoun : kind;
                classes[parsing.WordId] = kind;
                kinds[parsing.WordId] = kind == Pronoun ? kind : $"{kind} {parsing.Lemma}";
            }

            if (root.TryGetProperty(Suffix, out var suffix) && suffix.GetString() is { } agreement)
            {
                suffixes[parsing.WordId] = (agreement, parsing.Lemma);
            }

            if (root.TryGetProperty("refers", out var refers) && refers.GetString() == Suffix)
            {
                bySuffix.Add(parsing.WordId);
            }

            if (root.TryGetProperty(Language, out var language) && language.GetString() is { } code)
            {
                languages[parsing.WordId] = code;
            }
        }

        // "The Philistine" (הַפְּלִשְׁתִּי) referred to is one man, Goliath, not the Philistines: a singular gentilic stands for a
        // person only by a name of its own, never by its people.
        var people = named
            .Select(pair => (pair.Key, Card: pair.Value.FirstOrDefault(c => c.Role != WordEntityRoles.Title
                                                                            && !(c.Role == WordEntityRoles.People && members.Contains(pair.Key)))))
            .Where(pair => pair.Card is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Card!);
        var answers = new Dictionary<long, IReadOnlyList<ReferentResponse>>();
        foreach (var group in witnesses)
        {
            var found = new List<ReferentResponse>();
            // A word rendering the pronoun together with another word the source reads (відповів for λέγει αὐτῷ) is not the pronoun.
            var whole = group.All(witness => direct.ContainsKey(witness) || !classes.ContainsKey(witness));
            foreach (var witness in group.Distinct().Order().Where(direct.ContainsKey))
            {
                if (!whole || ReferredTo(direct, people, classes, bySuffix, witness) is not { } reached)
                {
                    whole = false;
                    break;
                }

                foreach (var (referent, source) in reached)
                {
                    var card = people[referent];
                    if (found.All(f => f.Slug != card.Slug))
                    {
                        found.Add(new ReferentResponse(card.Type, card.Slug, surfaces.GetValueOrDefault(referent) ?? "", source)
                        {
                            Suffix = bySuffix.Contains(witness) ? true : null,
                            Language = languages.GetValueOrDefault(witness),
                        });
                    }
                }
            }

            if (whole && found.Count > 0)
            {
                answers[group.Key] = found;
            }
        }

        var pronouns = answers.Count == 0 ? [] : await Pronouns(db, forms, cancellationToken);
        foreach (var word in RunWords(answers.Keys, witnesses, direct, classes, kinds, forms, heads, bySuffix, suffixes, pronouns))
        {
            answers.Remove(word);
        }

        foreach (var word in await Contradicting(db, answers, witnesses, direct, agreements, bySuffix, suffixes, pronouns, cancellationToken))
        {
            answers.Remove(word);
        }

        return answers;
    }

    private static readonly string PersonKind = EnumSpelling.Of(EntityKind.Person);

    /// <summary>What each word of the reading that is one of its language's pronouns says of whom it stands for (<see cref="PronounForms"/>).</summary>
    private static async Task<Dictionary<long, Agreement>> Pronouns(
        AppDbContext db,
        IReadOnlyDictionary<long, string> forms,
        CancellationToken cancellationToken)
    {
        var listed = forms.Where(pair => PronounForms.Listed(pair.Value)).Select(pair => pair.Key).ToList();
        if (listed.Count == 0)
        {
            return [];
        }

        var said = new Dictionary<long, Agreement>();
        foreach (var row in await db.Words
                     .Where(w => listed.Contains(w.Id))
                     .Select(w => new { w.Id, w.Text!.Language })
                     .ToListAsync(cancellationToken))
        {
            if (PronounForms.Of(row.Language, forms[row.Id]) is { } agreement)
            {
                said[row.Id] = agreement;
            }
        }

        return said;
    }

    /// <summary>
    /// The translation's words shown a person that their own form says cannot stand for the original's word they render:
    /// <em>them</em> or <em>ними</em> linked with <c>αὐτήν</c> (Tabitha), <em>her</em> with <c>αὐτοῦ</c> (Jesus), <em>thy</em>
    /// with <c>μου</c>. An aligner that links a pronoun with the original's pronoun beside it gives it that pronoun's
    /// person, and the forms are how it is told (<see cref="PronounForms"/>); a form its language's list lacks, or a language
    /// without a list, is not judged. Only a word standing for persons is: a pronoun for a city follows its own language's
    /// gender (<em>його</em> for Єрусалим, <c>αὐτῆς</c> in the Greek), and one for a people is plural as often as not.
    /// </summary>
    private static async Task<List<long>> Contradicting(
        AppDbContext db,
        Dictionary<long, IReadOnlyList<ReferentResponse>> answers,
        ILookup<long, long> witnesses,
        Dictionary<long, List<(long Referent, string Source)>> direct,
        Dictionary<long, Agreement> agreements,
        HashSet<long> bySuffix,
        Dictionary<long, (string Agreement, string? Lemma)> suffixes,
        Dictionary<long, Agreement> pronouns,
        CancellationToken cancellationToken)
    {
        var said = answers
            .Where(pair => pronouns.ContainsKey(pair.Key) && pair.Value.All(referent => referent.Type == PersonKind))
            .ToDictionary(pair => pair.Key, pair => pronouns[pair.Key]);
        if (said.Count == 0)
        {
            return [];
        }

        var rendered = said.Keys.ToDictionary(word => word, word => witnesses[word].Where(w => w != word && direct.ContainsKey(w)).Distinct().ToList());
        var unparsed = rendered.Values.SelectMany(list => list).Where(w => !bySuffix.Contains(w) && !agreements.ContainsKey(w)).Distinct().ToList();
        var parsed = new Dictionary<long, Agreement>(agreements);
        if (unparsed.Count > 0)
        {
            // BHSA's own parsing says a pronoun's person, number and gender, where MACULA Hebrew's says only that it is one.
            foreach (var row in await db.Words
                         .Where(w => unparsed.Contains(w.Id) && w.Morphology != null)
                         .Select(w => new { w.Id, w.Morphology })
                         .ToListAsync(cancellationToken))
            {
                parsed[row.Id] = Agreement.Of(row.Morphology!.RootElement);
            }
        }

        return
        [
            .. said.Where(pair => rendered[pair.Key].Any(original =>
                (bySuffix.Contains(original)
                    ? suffixes.TryGetValue(original, out var suffix) ? Agreement.OfSuffix(suffix.Agreement) : default
                    : parsed.GetValueOrDefault(original)) is var theirs
                && pair.Value.Contradicts(theirs)))
                .Select(pair => pair.Key),
        ];
    }

    /// <summary>
    /// Whether the word is a singular gentilic (BHSA's <c>lexicalSet: gntl</c>, <c>number: sg</c>): <em>the Philistine</em>,
    /// <em>the Hittite</em>, which the text uses for one man of that people as often as for the people.
    /// </summary>
    private static bool OneOfAPeople(JsonDocument? morphology) =>
        morphology?.RootElement is { ValueKind: JsonValueKind.Object } root
        && root.TryGetProperty("lexicalSet", out var set) && set.ValueKind == JsonValueKind.String && set.GetString() == "gntl"
        && root.TryGetProperty("number", out var number) && number.ValueKind == JsonValueKind.String && number.GetString() == "sg";

    /// <summary>
    /// The parsing's key for the person, gender and number of a pronoun suffix on the word (<c>3ms</c>), and its value of
    /// <c>refers</c> where the word refers by that suffix.
    /// </summary>
    private const string Suffix = "suffix";

    /// <summary>The source's class of a pronoun, by which a run of any pronoun is judged; any other kind of word by its lemma.</summary>
    private const string Pronoun = "pron";

    /// <summary>How often a word of a run must stand alone for the referring word's kind to keep the referent beside a word not judged.</summary>
    private const double PlainlyAlone = 0.5;

    /// <summary>
    /// The words given a referent that only go with the word rendering the referring word: a preposition or conjunction a
    /// translation writes in one run with it (<em>for</em> of "for me", <em>and</em> of "and I" for κἀγώ, <em>को</em> of
    /// "उस को"), which reaches the same referring word and would otherwise say whom it refers to too.
    /// <para>
    /// A run is the words of the text read that render nothing but referring words and reach one of them. Where the mapping
    /// marked a word of the run as going with another word of it (<see cref="LinkWordRole"/>: <em>to</em> attached to
    /// <em>him</em>), that word is not the pronoun. Where it did not (a source's own phrase alignment, a Strong-tagged text),
    /// what each word is decides, from the text's own links in the same reading: how often its form elsewhere stands alone for
    /// a word of the referring word's kind (any pronoun; any other word by its lemma, ἐκεῖ or ὧδε), among all its renderings
    /// of words the source classes. <em>me</em> stands alone for a pronoun again and again; <em>for</em> and <em>unto</em>
    /// never do, they come with one. The run's word standing alone most often keeps the referent; the others lose it. Nothing
    /// is guessed: where a word of the run has no other rendering to judge it by and the best of the rest does not plainly
    /// stand alone, or two words stand alone equally and not plainly, the whole run is given none.
    /// </para>
    /// </summary>
    private static HashSet<long> RunWords(
        IEnumerable<long> given,
        ILookup<long, long> witnesses,
        Dictionary<long, List<(long Referent, string Source)>> direct,
        Dictionary<long, string> classes,
        Dictionary<long, string> kinds,
        IReadOnlyDictionary<long, string> forms,
        ILookup<long, long> heads,
        HashSet<long> bySuffix,
        Dictionary<long, (string Agreement, string? Lemma)> suffixes,
        Dictionary<long, Agreement> pronouns)
    {
        var dropped = new HashSet<long>();

        // Every word of the reading by the original's word it reaches, and the candidates: the words that render nothing but
        // referring words (and words of other originals the source does not class). A word rendering the verb with the pronoun
        // (відповів for λέγει αὐτῷ) is no candidate.
        var renderers = new Dictionary<long, HashSet<long>>();
        var candidates = new Dictionary<long, List<long>>();
        foreach (var group in witnesses)
        {
            var rendered = group.Where(w => w != group.Key).Distinct().ToList();
            foreach (var witness in rendered)
            {
                (renderers.TryGetValue(witness, out var all) ? all : renderers[witness] = []).Add(group.Key);
            }

            if (!rendered.Any(direct.ContainsKey) || !rendered.All(w => direct.ContainsKey(w) || !classes.ContainsKey(w)))
            {
                continue;
            }

            foreach (var witness in rendered.Where(direct.ContainsKey))
            {
                (candidates.TryGetValue(witness, out var list) ? list : candidates[witness] = []).Add(group.Key);
            }
        }

        // What each form renders across the reading: how many of its words render words the source classes, and how many of
        // those stand alone (no other word of the reading renders what they render) for words of one kind, by that kind.
        var classedBy = new Dictionary<string, int>(StringComparer.Ordinal);
        var aloneBy = new Dictionary<(string Form, string Kind), int>();
        var alone = new Dictionary<long, string?>();
        foreach (var group in witnesses)
        {
            if (!forms.TryGetValue(group.Key, out var form))
            {
                continue;
            }

            var rendered = group.Where(w => w != group.Key && kinds.ContainsKey(w)).Distinct().ToList();
            if (rendered.Count == 0)
            {
                continue;
            }

            var kind = rendered.Select(w => kinds[w]).Distinct().Count() == 1 && rendered.All(w => renderers[w].Count == 1)
                ? kinds[rendered[0]]
                : null;
            alone[group.Key] = kind;
            classedBy[form] = classedBy.GetValueOrDefault(form) + 1;
            if (kind is not null)
            {
                aloneBy[(form, kind)] = aloneBy.GetValueOrDefault((form, kind)) + 1;
            }
        }

        double? Alone(long word, IReadOnlyCollection<string> kindsOfRun)
        {
            if (!forms.TryGetValue(word, out var form))
            {
                return null;
            }

            var classed = alone.TryGetValue(word, out var own);
            var others = classedBy.GetValueOrDefault(form) - (classed ? 1 : 0);
            if (others == 0)
            {
                return null;
            }

            var standing = kindsOfRun.Sum(kind => aloneBy.GetValueOrDefault((form, kind)))
                           - (own is not null && kindsOfRun.Contains(own) ? 1 : 0);
            return (double)standing / others;
        }

        foreach (var word in given)
        {
            var referring = witnesses[word].Where(w => w != word && direct.ContainsKey(w)).Distinct().ToList();
            if (referring.Any(bySuffix.Contains))
            {
                if (!SuffixWord(word, referring, witnesses, renderers, classes, forms, heads, suffixes, pronouns))
                {
                    dropped.Add(word);
                }

                continue;
            }

            var run = referring
                .SelectMany(w => candidates.GetValueOrDefault(w) ?? [])
                .Append(word)
                .Distinct()
                .ToList();
            if (run.Count < 2)
            {
                continue;
            }

            if (heads[word].Any(run.Contains))
            {
                dropped.Add(word);
                continue;
            }

            run.RemoveAll(other => other != word && heads[other].Any(run.Contains));
            if (run.Count < 2)
            {
                continue;
            }

            var kindsOfRun = referring.Select(w => kinds.GetValueOrDefault(w)).OfType<string>().Distinct().ToList();
            var standing = run.ToDictionary(w => w, w => Alone(w, kindsOfRun));
            var known = standing.Values.OfType<double>().ToList();
            if (standing[word] is not { } mine || mine < known.Max())
            {
                dropped.Add(word);
                continue;
            }

            var unknown = known.Count < run.Count;
            var shared = known.Count(value => value == mine) > 1;
            if (mine < PlainlyAlone && (unknown || shared))
            {
                dropped.Add(word);
            }
        }

        return dropped;
    }

    /// <summary>
    /// The classes of word a translation renders by the pronoun alone (<em>him</em> for <c>אֹתוֹ</c>, <em>йому</em> for
    /// <c>לוֹ</c>), so that the same lemma rendered again is a sign of the suffix; of any other class (a noun, a verb, the
    /// interjection <c>הִנֵּה</c>, whose <em>Here</em> of "Here am I" renders the particle) it is not.
    /// </summary>
    private static readonly HashSet<string> PronounCarriers = new(StringComparer.Ordinal) { "prep", "om" };

    /// <summary>
    /// Whether a word rendering an original's word that refers by a pronoun suffix (<c>מְלַאכְתּוֹ</c> "his work", by its
    /// <c>וֹ</c>) renders that suffix: <em>his</em>, not <em>work</em>; <em>him</em> of "saw him", not <em>saw</em>.
    /// <para>
    /// Every word of the reading rendering the original's word is weighed, and what decides is the text's own links in the
    /// same reading: of all its form's other renderings of words the source parses, how many render a word carrying a suffix
    /// of the same person, gender and number on another lemma (any lemma, where the host is a preposition or the object marker). <em>his</em> renders "his nostrils", "his father", "his
    /// wife" in the chapter, each with that suffix and none of them work; <em>work</em> renders only work. The word whose
    /// form does so most often keeps the referent, and only when it does so in at least half its renderings and no other
    /// word of the run does as often. A word the mapping attaches to another word of the run is not the suffix. Where the
    /// word renders more than one referring word of the original, nothing is shown.
    /// </para>
    /// <para>
    /// Where exactly one word of the run is one of its language's pronouns saying the suffix's person, number and gender
    /// (<see cref="PronounForms"/>), that word renders the suffix and no other does, however seldom its form occurs: <em>мої</em>
    /// of «дочки мої» for <c>בְנֹתַי</c>, whose form renders only that noun in the chapter, as a language that inflects its
    /// possessives splits one pronoun into many forms. Where two words of the run say it (<em>він</em> and <em>його</em> for a
    /// verb whose subject and suffix are of one person), the renderings decide as above.
    /// </para>
    /// </summary>
    private static bool SuffixWord(
        long word,
        List<long> referring,
        ILookup<long, long> witnesses,
        Dictionary<long, HashSet<long>> renderers,
        Dictionary<long, string> classes,
        IReadOnlyDictionary<long, string> forms,
        ILookup<long, long> heads,
        Dictionary<long, (string Agreement, string? Lemma)> suffixes,
        Dictionary<long, Agreement> pronouns)
    {
        if (referring.Count != 1 || !suffixes.TryGetValue(referring[0], out var host))
        {
            return false;
        }

        // Of a noun's or a verb's renderings, one on the same lemma is no sign of the suffix: "work" of "his work" thrice. A
        // preposition or the object marker carrying the suffix is rendered by the pronoun alone often enough (йому for לוֹ,
        // him for אֹתוֹ) that its own lemma counts.
        var content = !(classes.TryGetValue(referring[0], out var kind) && PronounCarriers.Contains(kind));
        var run = (renderers.GetValueOrDefault(referring[0]) ?? []).Append(word).Distinct().ToList();
        if (heads[word].Any(run.Contains))
        {
            return false;
        }

        run.RemoveAll(other => other != word && heads[other].Any(run.Contains));
        var suffix = Agreement.OfSuffix(host.Agreement);
        var saying = run.Where(other => pronouns.TryGetValue(other, out var says) && says.Says(suffix)).ToList();
        if (saying.Count == 1)
        {
            return saying[0] == word;
        }

        double? Standing(long candidate)
        {
            if (!forms.TryGetValue(candidate, out var form))
            {
                return null;
            }

            var (renderings, agreeing) = (0, 0);
            foreach (var group in witnesses)
            {
                if (group.Key == candidate || !forms.TryGetValue(group.Key, out var other) || other != form)
                {
                    continue;
                }

                var rendered = group.Where(w => w != group.Key).Distinct().ToList();
                if (!rendered.Any(classes.ContainsKey))
                {
                    continue;
                }

                renderings++;
                if (rendered.Any(w => suffixes.TryGetValue(w, out var suffix) && suffix.Agreement == host.Agreement
                                      && (!content || suffix.Lemma != host.Lemma)))
                {
                    agreeing++;
                }
            }

            return renderings == 0 ? null : (double)agreeing / renderings;
        }

        if (Standing(word) is not { } mine || mine < PlainlyAlone)
        {
            return false;
        }

        return run.Where(other => other != word).All(other => Standing(other) is not { } theirs || theirs < mine);
    }

    /// <summary>
    /// The words naming a person that one word refers to, nearest first, each with the source of the step that reached it; null
    /// where the word is a noun, or where any branch of its references ends at a word naming nobody that is no pronoun.
    /// </summary>
    private static List<(long Referent, string Source)>? ReferredTo(
        Dictionary<long, List<(long Referent, string Source)>> direct,
        Dictionary<long, EntityRefResponse> people,
        Dictionary<long, string> classes,
        HashSet<long> bySuffix,
        long word)
    {
        if (classes.GetValueOrDefault(word) == "noun" && !bySuffix.Contains(word))
        {
            return null;
        }

        var reached = new List<(long, string)>();
        var seen = new HashSet<long> { word };
        var queue = new Queue<(long Word, int Step)>([(word, 0)]);
        while (queue.Count > 0)
        {
            var (at, step) = queue.Dequeue();
            foreach (var (referent, source) in direct.GetValueOrDefault(at) ?? [])
            {
                if (!seen.Add(referent))
                {
                    continue;
                }

                if (people.ContainsKey(referent))
                {
                    reached.Add((referent, source));
                }
                else if (step + 1 < ReferenceSteps && direct.ContainsKey(referent)
                         && classes.TryGetValue(referent, out var kind) && Referring.Contains(kind))
                {
                    queue.Enqueue((referent, step + 1));
                }
                else
                {
                    return null;
                }
            }
        }

        return reached;
    }

    /// <summary>
    /// The Strong number the corpus proposed for each word, where it proposed one and only one.
    ///
    /// A word whose lemma two dictionary entries claim has two proposals, and choosing between them
    /// here would be a guess sent out as an answer; it gets none. Two methods proposing the same
    /// number are one answer, and the surer of them describes it.
    /// </summary>
    private static async Task<Dictionary<long, StrongCandidateResponse>> Proposed(
        AppDbContext db,
        List<long> ids,
        CancellationToken cancellationToken)
    {
        var proposals = await db.WordStrongs
            .Where(proposal => ids.Contains(proposal.WordId))
            .Select(proposal => new
            {
                proposal.WordId, proposal.Number, proposal.Method, proposal.Confidence, proposal.Source,
            })
            .ToListAsync(cancellationToken);

        return proposals
            .GroupBy(proposal => proposal.WordId)
            .Where(group => group.Select(proposal => proposal.Number).Distinct().Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(proposal => proposal.Confidence is null)
                    .ThenByDescending(proposal => proposal.Confidence)
                    .ThenByDescending(proposal => LinkMethodPrecedence.Of(proposal.Method))
                    .ThenBy(proposal => proposal.Source, StringComparer.Ordinal)
                    .Select(proposal => new StrongCandidateResponse(
                        proposal.Number, EnumSpelling.Of(proposal.Method), proposal.Confidence))
                    .First());
    }

    /// <summary>
    /// What established this word's link, in one string the client can show without reading the
    /// schema: the method, and the confidence where there is one. A link with no confidence is one
    /// somebody asserted, and it is the only kind that carries no number — which is the point, and
    /// why the number is not defaulted to 1.
    /// </summary>
    private static string Provenance(LinkMethod method, double? confidence) =>
        confidence is { } value
            ? $"{EnumSpelling.Of(method)}:{value.ToString("0.##", CultureInfo.InvariantCulture)}"
            : EnumSpelling.Of(method);

    /// <param name="Witnesses">The witness words each word reaches, which is what the client intersects.</param>
    /// <param name="Provenance">What established the strongest link on each word, where it has one.</param>
    /// <param name="Absent">
    /// Where a link records an absence rather than a correspondence: <c>expands</c> for a word this
    /// text supplies and the other does not have, <c>omits</c> for the reverse.
    /// </param>
    /// <param name="Marks">
    /// What the edition itself prints about each word it marks, every mark in kind order: supplied, doubtful, a subscription, a
    /// corrupt place. A word can stand in two, as Charles's supplied words inside his interpolations do. It is not <paramref name="Absent"/> read
    /// twice: that is what an alignment found, and this is what the edition printed, which is a
    /// first-hand claim about one text and names no counterpart for it.
    /// </param>
    /// <param name="Named">
    /// Every record each word names, the first answer first, where the corpus can say and can say
    /// it without choosing. Empty for most words, because most words are not names.
    /// </param>
    /// <param name="Proposed">
    /// The one Strong number the corpus proposed for each word, where it proposed exactly one.
    /// </param>
    private sealed record Reached(
        ILookup<long, long> Witnesses,
        Dictionary<long, string> Provenance,
        Dictionary<long, string> Absent,
        Dictionary<long, WordGroupKind[]> Marks,
        Dictionary<long, IReadOnlyList<EntityRefResponse>> Named,
        Dictionary<long, StrongCandidateResponse> Proposed)
    {
        /// <summary>The lexicon's gloss for each Greek word its edition does not gloss, and how it was reached.</summary>
        public Dictionary<long, LexiconGlossResponse> Glossed { get; init; } = [];

        /// <summary>For each Ge'ez word, the meaning of the Greek word it is aligned to.</summary>
        public Dictionary<long, ThroughGreekResponse> ThroughGreek { get; init; } = [];

        /// <summary>For each Ge'ez word, the entry of Dillmann's lexicon it is a form of.</summary>
        public Dictionary<long, GeezEntryResponse> GeezEntries { get; init; } = [];

        /// <summary>For each Ge'ez word a model read in its verse, what it read.</summary>
        public Dictionary<long, GeezGlossResponse> GeezGlosses { get; init; } = [];

        /// <summary>For each word that goes with another word of its text, that word and how.</summary>
        public Dictionary<long, AttachedResponse> Attached { get; init; } = [];

        /// <summary>The letters the edition prints between its signs inside each word that has them.</summary>
        public Dictionary<long, string> Bracketed { get; init; } = [];

        /// <summary>For each word naming a person's occupation in a verse that says it, whose and which.</summary>
        public Dictionary<long, IReadOnlyList<WordRoleResponse>> Roles { get; init; } = [];

        /// <summary>
    /// For each word that is, or renders, a word referring back to another, the records that word names. A word that names a
    /// record itself is given none (<see cref="Word"/>): a translation that writes the name has said it, and a name linked
    /// together with a pronoun (<em>Jesus</em> of "Jesus answered him") is not the pronoun.
    /// </summary>
        public Dictionary<long, IReadOnlyList<ReferentResponse>> Referents { get; init; } = [];
    }

    private sealed record GlossWanted(
        long Id, string Surface, string? Folded, string? Gloss, string? Lemma, string? StrongNumber, int VerseId);

    /// <summary>
    /// A short gloss for every Greek word whose edition prints none, from the lexicon, and the way
    /// each was reached — because the ways are not equally strong and a reader is owed the difference.
    ///
    /// Tried in order of how little stands between the word and the entry: the number the edition
    /// itself prints; then the word's own dictionary form; then, for a word that has neither, the
    /// dictionary form of the word it is linked to as the same word in another edition — Swete's
    /// Septuagint reaches Brenton's lemmas that way; then the one number the corpus proposed from
    /// the word's form. A form or a number the lexicon files under several entries gets every one of
    /// their glosses, in the lexicon's order, rather than one of them chosen here.
    ///
    /// Last, for a word none of those reached, its spelling: the dictionary form every other Greek
    /// word printed exactly the same way carries, where they all carry the same one the lexicon
    /// holds. Case and a grave accent for an acute do not count as a different spelling; a breathing
    /// or another accent does, which is what keeps Ἰού the name apart from ἰοῦ the noun. Where the
    /// words spelt that way disagree, the word gets nothing.
    ///
    /// Five queries for a chapter at most, and none at all for a text that is not Greek.
    /// </summary>
    private static async Task<Dictionary<long, LexiconGlossResponse>> Glossed(
        AppDbContext db,
        int textId,
        IReadOnlyList<GlossWanted> words,
        Dictionary<long, StrongCandidateResponse> proposed,
        CancellationToken cancellationToken)
    {
        var wanting = words.Where(word => word.Gloss is null).ToList();
        if (wanting.Count == 0
            || await db.Texts.Where(text => text.Id == textId).Select(text => text.Language)
                .SingleAsync(cancellationToken) != Greek)
        {
            return [];
        }

        var bare = wanting.Where(word => word.Lemma is null && word.StrongNumber is null).Select(word => word.Id).ToList();
        var bareVerses = wanting.Where(word => word.Lemma is null && word.StrongNumber is null).Select(word => word.VerseId).Distinct().ToList();
        var borrowed = bare.Count == 0
            ? []
            : (await db.LinkWords
                    .Where(side => bareVerses.Contains(side.VerseId) && bare.Contains(side.WordId) && side.Link!.Relation == LinkRelation.Equals)
                    .SelectMany(side => db.LinkWords
                        .Where(other => other.LinkId == side.LinkId
                                        && other.Side != side.Side
                                        && other.Word!.Lemma != null)
                        .Select(other => new { side.WordId, other.Word!.Lemma }))
                    .ToListAsync(cancellationToken))
                .GroupBy(row => row.WordId)
                .Where(group => group.Select(row => row.Lemma).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => group.First().Lemma!);

        var lemmas = wanting.Select(word => word.Lemma).Concat(borrowed.Values).OfType<string>().Distinct().ToList();
        var numbers = wanting
            .Select(word => word.StrongNumber ?? proposed.GetValueOrDefault(word.Id)?.Number)
            .OfType<string>()
            .Distinct()
            .ToList();

        var entries = await db.LexiconGlosses
            .Where(gloss => lemmas.Contains(gloss.Lemma) || numbers.Contains(gloss.StrongNumber))
            .OrderBy(gloss => gloss.Id)
            .Select(gloss => new { gloss.Lemma, gloss.StrongNumber, gloss.Gloss })
            .ToListAsync(cancellationToken);
        var byLemma = entries.ToLookup(entry => entry.Lemma, StringComparer.Ordinal);
        var byNumber = entries.ToLookup(entry => entry.StrongNumber, StringComparer.Ordinal);

        static LexiconGlossResponse? Answer(IEnumerable<string> glosses, string via) =>
            glosses.Distinct(StringComparer.Ordinal).ToArray() is { Length: > 0 } found
                ? new LexiconGlossResponse(found, via)
                : null;

        var glossed = new Dictionary<long, LexiconGlossResponse>();
        foreach (var word in wanting)
        {
            // A number filed under several entries is narrowed by the word's own form where the
            // edition prints one and it names one of them; otherwise every entry under the number stands.
            var stated = word.StrongNumber is { } number ? byNumber[number].ToList() : [];
            var own = stated.Where(entry => entry.Lemma == word.Lemma).ToList();

            var answer =
                Answer((own.Count > 0 ? own : stated).Select(entry => entry.Gloss), ByStatedNumber)
                ?? (word.Lemma is { } lemma ? Answer(byLemma[lemma].Select(entry => entry.Gloss), ByLemma) : null)
                ?? (borrowed.TryGetValue(word.Id, out var equal)
                    ? Answer(byLemma[equal].Select(entry => entry.Gloss), ByEqualWord)
                    : null)
                ?? (word.StrongNumber is null && proposed.GetValueOrDefault(word.Id) is { } candidate
                    ? Answer(byNumber[candidate.Number].Select(entry => entry.Gloss), ByProposedNumber)
                    : null);

            if (answer is not null)
            {
                glossed[word.Id] = answer;
            }
        }

        var unreached = wanting.Where(word => !glossed.ContainsKey(word.Id) && word.Folded is { Length: > 0 }).ToList();
        if (unreached.Count == 0)
        {
            return glossed;
        }

        var forms = unreached.Select(word => word.Folded!).Distinct().ToList();
        var spelt = await db.Words
            .Where(other => other.Lemma != null
                            && forms.Contains(other.NormalisedText!)
                            && other.Text!.Language == Greek
                            && db.LexiconGlosses.Any(gloss => gloss.Lemma == other.Lemma))
            .Select(other => new { other.Surface, other.Lemma })
            .Distinct()
            .ToListAsync(cancellationToken);
        var lemmaBySpelling = spelt
            .GroupBy(other => GreekLetters.Spelling(other.Surface), StringComparer.Ordinal)
            .Where(group => group.Select(other => other.Lemma).Distinct().Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Lemma!, StringComparer.Ordinal);

        var further = lemmaBySpelling.Values.Distinct().Where(lemma => !byLemma.Contains(lemma)).ToList();
        var furtherEntries = further.Count == 0
            ? []
            : await db.LexiconGlosses
                .Where(gloss => further.Contains(gloss.Lemma))
                .OrderBy(gloss => gloss.Id)
                .Select(gloss => new { gloss.Lemma, gloss.Gloss })
                .ToListAsync(cancellationToken);
        var bySpeltLemma = entries.Select(entry => new { entry.Lemma, entry.Gloss })
            .Concat(furtherEntries)
            .ToLookup(entry => entry.Lemma, StringComparer.Ordinal);

        foreach (var word in unreached)
        {
            if (lemmaBySpelling.TryGetValue(GreekLetters.Spelling(word.Surface), out var lemma)
                && Answer(bySpeltLemma[lemma].Select(entry => entry.Gloss), BySpelling) is { } answer)
            {
                glossed[word.Id] = answer;
            }
        }

        return glossed;
    }

    private sealed record AlignedGreek(
        long WordId, string Corpus, string Surface, string? Lemma, string? StrongNumber, string? Gloss,
        LinkMethod Method, double? Confidence);

    /// <summary>
    /// What a Ge'ez word means, two ways, for a text in Ge'ez and nothing else.
    ///
    /// Through the Greek: the meaning of the Greek word the aligner put beside it — its edition's
    /// gloss, or the Greek lexicon's by the number its edition prints or its dictionary form — from
    /// the surest link that reaches a Greek word with a meaning. It is carried with the Greek word
    /// and the link, because it says what the Greek means and that the aligner paired the two, and
    /// nothing about the Ge'ez itself.
    ///
    /// From Dillmann: the entry the word is a form of, where <see cref="GeezLexicon"/> can say so
    /// without choosing, the aligned Greek words' forms serving as the evidence where several
    /// headwords could be meant.
    ///
    /// In context: what a language model read the word as in its verse, beside the Greek, where a
    /// run has read it, with the Greek words it named.
    ///
    /// Six queries for a chapter, and one for a text in another language.
    /// </summary>
    private static async Task<Reached> Meant(
        AppDbContext db,
        int textId,
        IReadOnlyList<(long Id, int Verse, string Surface)> words,
        Reached counterparts,
        CancellationToken cancellationToken)
    {
        if (words.Count == 0
            || await db.Texts.Where(text => text.Id == textId).Select(text => text.Language)
                .SingleAsync(cancellationToken) != Geez)
        {
            return counterparts;
        }

        var ids = words.Select(word => word.Id).ToList();
        var verses = words.Select(word => word.Verse).Distinct().ToList();
        var aligned = await db.LinkWords
            .Where(side => verses.Contains(side.VerseId)
                           && side.Link!.Relation != LinkRelation.Expands
                           && side.Link.Relation != LinkRelation.Omits)
            .SelectMany(side => db.LinkWords
                .Where(other => other.LinkId == side.LinkId
                                && other.Side != side.Side
                                && other.Word!.Text!.Language == Greek)
                .Select(other => new AlignedGreek(
                    side.WordId, other.Word!.Text!.Slug, other.Word.Surface, other.Word.Lemma,
                    other.Word.StrongNumber, other.Word.Gloss, side.Link!.Method, side.Link.Confidence)))
            .ToListAsync(cancellationToken);

        var lemmas = aligned.Select(greek => greek.Lemma).OfType<string>().Distinct().ToList();
        var numbers = aligned.Select(greek => greek.StrongNumber).OfType<string>().Distinct().ToList();
        var entries = lemmas.Count == 0 && numbers.Count == 0
            ? []
            : await db.LexiconGlosses
                .Where(gloss => lemmas.Contains(gloss.Lemma) || numbers.Contains(gloss.StrongNumber))
                .OrderBy(gloss => gloss.Id)
                .Select(gloss => new { gloss.Lemma, gloss.StrongNumber, gloss.Gloss })
                .ToListAsync(cancellationToken);
        var byLemma = entries.ToLookup(entry => entry.Lemma, entry => entry.Gloss, StringComparer.Ordinal);
        var byNumber = entries.ToLookup(entry => entry.StrongNumber, StringComparer.Ordinal);

        // The edition's own gloss first; then the entries under the number it prints, narrowed by its
        // dictionary form where that names one of them; then its dictionary form alone.
        string[] MeaningOf(AlignedGreek greek)
        {
            if (greek.Gloss is { Length: > 0 } own)
            {
                return [own];
            }

            var stated = greek.StrongNumber is { } number ? byNumber[number].ToList() : [];
            var narrowed = stated.Where(entry => entry.Lemma == greek.Lemma).ToList();
            return
            [
                .. (narrowed.Count > 0 ? narrowed : stated).Select(entry => entry.Gloss)
                    .Concat(stated.Count == 0 && greek.Lemma is { } lemma ? byLemma[lemma] : [])
                    .Distinct(StringComparer.Ordinal),
            ];
        }

        var byWord = aligned.ToLookup(greek => greek.WordId);
        var throughGreek = new Dictionary<long, ThroughGreekResponse>();
        foreach (var id in ids)
        {
            var surest = byWord[id]
                .OrderByDescending(greek => greek.Confidence is null)
                .ThenByDescending(greek => greek.Confidence)
                .ThenBy(greek => greek.Corpus, StringComparer.Ordinal)
                .Select(greek => (Greek: greek, Glosses: MeaningOf(greek)))
                .FirstOrDefault(found => found.Glosses.Length > 0);
            if (surest.Greek is { } link)
            {
                throughGreek[id] = new ThroughGreekResponse(
                    link.Corpus, link.Surface, link.Lemma, surest.Glosses, EnumSpelling.Of(link.Method), link.Confidence);
            }
        }

        var keys = words.SelectMany(word => GeezLexicon.KeysOf(word.Surface)).Distinct().ToList();
        var headwords = keys.Count == 0
            ? []
            : await db.GeezLexiconEntries
                .Where(entry => entry.Consonants.Any(consonants => keys.Contains(consonants)))
                .ToListAsync(cancellationToken);
        var geezEntries = new Dictionary<long, GeezEntryResponse>();
        if (headwords.Count > 0)
        {
            var lexicon = new GeezLexicon(headwords.Select(entry => new GeezHeadword(entry.Entry, entry.Forms, entry.Greek)));
            var byEntry = headwords.ToDictionary(entry => entry.Entry, StringComparer.Ordinal);
            foreach (var (id, _, surface) in words)
            {
                var greek = byWord[id].SelectMany(link => new[] { link.Lemma, link.Surface }).OfType<string>().ToList();
                if (lexicon.Match(surface, greek) is not { } match)
                {
                    continue;
                }

                var entry = byEntry[match.Entry];
                geezEntries[id] = new GeezEntryResponse(
                    entry.Entry,
                    entry.Headword,
                    [.. entry.Latin.Take(LatinShown)],
                    [
                        .. entry.Greek
                            .Where(equivalent => equivalent.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= GreekShownWords)
                            .Take(GreekShown),
                    ],
                    match.Via);
            }
        }

        var glosses = await db.GeezWordGlosses.Where(gloss => ids.Contains(gloss.WordId)).ToListAsync(cancellationToken);
        var named = glosses.SelectMany(gloss => gloss.GreekWordIds).Distinct().ToList();
        var greekWords = named.Count == 0
            ? []
            : await db.Words.Where(word => named.Contains(word.Id))
                .Select(word => new { word.Id, word.Surface })
                .ToDictionaryAsync(word => word.Id, word => word.Surface, cancellationToken);
        var geezGlosses = glosses.ToDictionary(
            gloss => gloss.WordId,
            gloss => new GeezGlossResponse(
                gloss.Lemma,
                gloss.LemmaSource,
                new GeezGlossLanguages(gloss.SenseEn, gloss.SenseUk),
                new GeezGlossLanguages(gloss.LineEn, gloss.LineUk),
                [.. gloss.GreekWordIds.Select(greekWords.GetValueOrDefault).OfType<string>()],
                gloss.Morphology,
                gloss.Confidence,
                gloss.Model,
                gloss.Made.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        return counterparts with { ThroughGreek = throughGreek, GeezEntries = geezEntries, GeezGlosses = geezGlosses };
    }

    /// <summary>Reads the verses of one text that sit at the given canonical addresses.</summary>
    public static async Task<Dictionary<int, List<TextWordResponse>>> ReadByCanonicalVerse(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary
                        && r.Verse!.TextId == textId
                        && r.CanonicalBook == canonicalBook
                        && r.CanonicalChapter == canonicalChapter)
            .SelectMany(r => r.Verse!.Words.Select(w => new CanonicalWordRow(
                r.CanonicalVerse,
                new EditionPlace(r.Verse.Book!.Position, r.Verse.ChapterNumber, r.Verse.Sequence, r.Verse.Number, r.Verse.Label),
                w.Position, w.Id, w.Surface, w.Trailer, w.Gloss,
                w.Lemma, w.StrongNumber, w.Morphology, w.Elided, w.Break, w.NormalisedText, w.VerseId)))
            .ToListAsync(cancellationToken);

        var counterparts = await Counterparts(db, rows.Select(r => (r.Id, r.VerseId, r.Text)), cancellationToken);
        counterparts = counterparts with
        {
            Glossed = await Glossed(
                db, textId,
                [.. rows.Select(r => new GlossWanted(r.Id, r.Text, r.NormalisedText, r.Gloss, r.Lemma, r.StrongNumber, r.VerseId))],
                counterparts.Proposed, cancellationToken),
        };
        counterparts = await Meant(db, textId, [.. rows.Select(r => (r.Id, r.VerseId, r.Text))], counterparts, cancellationToken);

        return rows
            .GroupBy(r => r.CanonicalVerse)
            .ToDictionary(
                group => group.Key,
                // Two verses of this text can sit at one canonical address, and ordering by word
                // position alone shuffles their words together, so the verses go in the edition's
                // own order first.
                group => group
                    .OrderBy(r => r.Place).ThenBy(r => r.Position)
                    .Select(r => Word(r.Id, r.Text, r.Trailer, r.Gloss, r.Lemma, r.StrongNumber, r.Morphology,
                        r.Elided, r.Break, counterparts))
                    .ToList());
    }

    /// <summary>
    /// Grouped by the number **and** the letter, because the Septuagint prints Genesis 31 as 49,
    /// 50, 50a, 52 and two verses numbered 50 are two verses. The separate verse list is what
    /// lets a note-only verse remain visible rather than being lost from a query rooted at words.
    /// </summary>
    private static IList<TextVerseResponse> Group(
        List<VerseRow> verses,
        List<WordRow> rows,
        Reached counterparts,
        List<NoteRow>? notes = null)
    {
        var notesByVerse = (notes ?? [])
            .ToLookup(note => (note.VerseNumber, note.Label), note => new SourceNoteResponse(
                EnumSpelling.Of(note.Kind), note.Content, note.AnchorWordId, note.Anchor));
        var wordsByVerse = rows.ToLookup(row => (row.VerseNumber, row.Label));

        return verses
            .Select(verse => new TextVerseResponse(
                verse.Number,
                wordsByVerse[(verse.Number, verse.Label)].Select(row => Word(
                        row.Id, row.Text, row.Trailer, row.Gloss, row.Lemma, row.StrongNumber, row.Morphology,
                        row.Elided, row.Break, counterparts))
                    .ToList(),
                verse.Label)
            {
                Notes = [.. notesByVerse[(verse.Number, verse.Label)]],
            })
            .ToList();
    }

    private static TextWordResponse Word(
        long id,
        string text,
        string trailer,
        string? gloss,
        string? lemma,
        string? strongNumber,
        JsonDocument? morphology,
        bool elided,
        TextBreak? opening,
        Reached counterparts)
    {
        var features = Features(morphology);
        return new TextWordResponse(
            id,
            text,
            trailer,
            gloss,
            lemma,
            strongNumber,
            [.. counterparts.Witnesses[id].Distinct()],
            counterparts.Provenance.GetValueOrDefault(id),
            counterparts.Absent.GetValueOrDefault(id),
            counterparts.Named.GetValueOrDefault(id)?[0],
            Morphology(features),
            Feature(features, Phono),
            Feature(features, PhonoTrailer),
            Feature(features, Language))
        {
            Elided = elided,
            Supplied = counterparts.Marks.TryGetValue(id, out var marks) && marks.Contains(WordGroupKind.Supplied),
            Mark = marks is null ? null : string.Join(' ', marks.Select(EnumSpelling.Of)),
            Bracketed = counterparts.Bracketed.GetValueOrDefault(id),
            StrongCandidate = strongNumber is null ? counterparts.Proposed.GetValueOrDefault(id) : null,
            LexiconGloss = counterparts.Glossed.GetValueOrDefault(id),
            ThroughGreek = counterparts.ThroughGreek.GetValueOrDefault(id),
            GeezEntry = counterparts.GeezEntries.GetValueOrDefault(id),
            GeezGloss = counterparts.GeezGlosses.GetValueOrDefault(id),
            Attached = counterparts.Attached.GetValueOrDefault(id),
            Roles = counterparts.Roles.GetValueOrDefault(id),
            Referents = counterparts.Named.ContainsKey(id) ? null : counterparts.Referents.GetValueOrDefault(id),
            Break = opening is { } kind ? EnumSpelling.Of(kind) : null,
            Entities = counterparts.Named.GetValueOrDefault(id) ?? [],
        };
    }

    private static JsonElement? Features(JsonDocument? morphology) => morphology?.RootElement;

    private static string? Feature(JsonElement? features, string name) =>
        features is { } element && element.TryGetProperty(name, out var value) ? value.GetString() : null;

    /// <summary>
    /// The annotation, as much of it as the old contract has a field for. A word with none — every
    /// word of every translation — answers null rather than an object of nulls.
    /// </summary>
    private static MorphologyResponse? Morphology(JsonElement? features)
    {
        if (features is null || Feature(features, PartOfSpeech) is null)
        {
            return null;
        }

        return new MorphologyResponse(
            Feature(features, PartOfSpeech),
            Feature(features, "case"),
            Feature(features, "gender"),
            Feature(features, "number"),
            Feature(features, "person"),
            Feature(features, "state"),
            Feature(features, "stem"),
            Feature(features, "tense"),
            Feature(features, "lexicalSet"),
            Feature(features, "phrasePos"),
            Feature(features, "suffixGender"),
            Feature(features, "suffixNumber"),
            Feature(features, "suffixPerson"),
            Feature(features, "nameType")?.Split(','));
    }

    private sealed record WordRow(
        int VerseNumber, string Label, long Id, string Text, string Trailer, string? Gloss, string? Lemma,
        string? StrongNumber, JsonDocument? Morphology, bool Elided, TextBreak? Break, string? NormalisedText, int VerseId);

    private sealed record VerseRow(int Number, string Label);

    private sealed record NoteRow(
        int VerseNumber,
        string Label,
        VerseNoteKind Kind,
        string Content,
        long? AnchorWordId,
        string? Anchor);

    private sealed record CanonicalWordRow(
        int CanonicalVerse, EditionPlace Place, int Position, long Id, string Text, string Trailer,
        string? Gloss, string? Lemma, string? StrongNumber, JsonDocument? Morphology, bool Elided, TextBreak? Break,
        string? NormalisedText, int VerseId);

    /// <summary>
    /// The chapters a chapter's rows are read from: itself, and each chapter holding a passage of it
    /// under another name (<see cref="TwinPassages"/>) — the Letter of Jeremiah, which is Baruch 6 in the
    /// Latin and English Bibles and a book of its own in the Greek, and the Song of the Three, which is
    /// inside Daniel 3 in the Greek and Latin and a book of its own in the King James. A text is read
    /// under whichever name it prints the passage, with the verses of that passage and at the rows they
    /// have here, so either name opens it in every text that has it.
    /// </summary>
    internal static IEnumerable<(int Book, int Chapter, int First, int Last, int Shift)> Held(int book, int chapter) =>
        [(book, chapter, int.MinValue, int.MaxValue, 0), .. TwinPassages.NamedElsewhere(book, chapter)];

    /// <summary>
    /// Rows read under another name, at the rows they have here; a row the chapter already holds for the
    /// text keeps what it has, since a verse stands primarily under one name only.
    /// </summary>
    internal static void Merge<T>(Dictionary<int, T> into, Dictionary<int, T> read, int first, int last, int shift)
    {
        foreach (var (verse, value) in read)
        {
            if (verse >= first && verse <= last)
            {
                into.TryAdd(verse + shift, value);
            }
        }
    }

    /// <summary>
    /// Which of a text's own verses sit at each canonical address in this chapter, as the text
    /// numbers them.
    ///
    /// <see cref="OwnReferences"/> answers the same question and keeps only one per address, because
    /// a reference is a single address and cannot be two. That is right for what it is for and it is
    /// why the label went missing: Brenton's <c>31:50a</c> and <c>31:50</c> both belong at canonical
    /// 31:50, so one of the two was reported and the row silently held both.
    /// </summary>
    public static async Task<Dictionary<int, List<string>>> OwnVerses(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary
                        && r.Verse!.TextId == textId
                        && r.CanonicalBook == canonicalBook
                        && r.CanonicalChapter == canonicalChapter)
            .Select(r => new
            {
                r.CanonicalVerse,
                Place = new EditionPlace(r.Verse!.Book!.Position, r.Verse.ChapterNumber, r.Verse.Sequence, r.Verse.Number, r.Verse.Label),
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.CanonicalVerse)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(r => r.Place)
                    .Order()
                    .Select(place => $"{place.Chapter}:{place.Number}{place.Label}")
                    .ToList());
    }

    /// <summary>
    /// What the edition prints as its own address for each canonical address in this chapter.
    ///
    /// <para>
    /// It sits beside <see cref="OwnVerses"/> and answers a different question. That one asks which
    /// verses this text holds here, and every answer is a row that can be fetched; this one asks
    /// what the edition's own pages call them, and the answer is a claim about a printed book that
    /// no row of this corpus answers to. Reading them as one field is how the Synodal would come to
    /// offer 118:1 as a verse it has.
    /// </para>
    ///
    /// <para>
    /// The addresses of a verse are ordered as the edition printed them, and where two of its
    /// verses became one of ours both are here — the second is not a duplicate of the first and
    /// dropping it would say the edition divides the passage as we do.
    /// </para>
    /// </summary>
    public static async Task<Dictionary<int, List<string>>> StatedVerses(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.StatedVerseNumbers
            .Where(n => n.Verse!.TextId == textId
                        && n.Verse.References.Any(r => r.IsPrimary
                                                       && r.CanonicalBook == canonicalBook
                                                       && r.CanonicalChapter == canonicalChapter))
            .Select(n => new
            {
                Canonical = n.Verse!.References.First(r => r.IsPrimary).CanonicalVerse,
                Holder = new EditionPlace(n.Verse.Book!.Position, n.Verse.ChapterNumber, n.Verse.Sequence, n.Verse.Number, n.Verse.Label),
                n.Position,
                Chapter = n.ChapterNumber,
                n.ChapterLabel,
                Verse = n.Number,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Canonical)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(row => row.Holder).ThenBy(row => row.Position)
                    .Select(row => $"{row.Chapter}{row.ChapterLabel}:{row.Verse}")
            .ToList());
    }

    /// <summary>
    /// The edition's notes, grouped by the canonical row rather than by their printed number. A
    /// row can hold more than one of a translation's own verses, so the order keeps the verse they
    /// belong to before their position inside it.
    /// </summary>
    public static async Task<Dictionary<int, List<SourceNoteResponse>>> SourceNotes(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseNotes
            .Where(note => note.Verse!.TextId == textId
                           && note.Verse.References.Any(reference => reference.IsPrimary
                                                                    && reference.CanonicalBook == canonicalBook
                                                                    && reference.CanonicalChapter == canonicalChapter))
            .Select(note => new
            {
                Canonical = note.Verse!.References.First(reference => reference.IsPrimary).CanonicalVerse,
                Holder = new EditionPlace(
                    note.Verse.Book!.Position, note.Verse.ChapterNumber, note.Verse.Sequence, note.Verse.Number, note.Verse.Label),
                note.Position,
                note.Kind,
                note.Content,
                note.AnchorWordId,
                Anchor = note.AnchorWord == null ? null : note.AnchorWord.Surface,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Canonical)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(row => row.Holder).ThenBy(row => row.Position)
                    .Select(row => new SourceNoteResponse(
                        EnumSpelling.Of(row.Kind), row.Content, row.AnchorWordId, row.Anchor))
                    .ToList());
    }

    /// <summary>What each text calls the verses it puts at these canonical addresses.</summary>
    public static async Task<Dictionary<int, VerseRefResponse>> OwnReferences(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary
                        && r.Verse!.TextId == textId
                        && r.CanonicalBook == canonicalBook
                        && r.CanonicalChapter == canonicalChapter)
            .Select(r => new
            {
                r.CanonicalVerse,
                Place = new EditionPlace(r.Verse!.Book!.Position, r.Verse.ChapterNumber, r.Verse.Sequence, r.Verse.Number, r.Verse.Label),
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.CanonicalVerse)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.Min(r => r.Place);
                    return new VerseRefResponse(
                        canonicalBook,
                        BookReferences.Name(canonicalBook),
                        BookReferences.Slug(canonicalBook),
                        first.Chapter,
                        first.Number);
                });
    }

    /// <summary>
    /// Where a verse stands in its edition: its book in the edition's order of books, its chapter, where the edition
    /// writes it in that chapter, then its number and letter. A text can put several of its verses at one canonical
    /// address (Revelation 12:18 and 13:1 at 13:1, Brenton's Exodus 37:19 and 38:19 at 38:19), and everything that lists
    /// them there lists them in this order, which is the edition's and is the same every time it is read.
    /// </summary>
    internal readonly record struct EditionPlace(int Book, int Chapter, int Sequence, int Number, string Label)
        : IComparable<EditionPlace>
    {
        public int CompareTo(EditionPlace other)
        {
            var order = (Book, Chapter, Sequence, Number).CompareTo((other.Book, other.Chapter, other.Sequence, other.Number));
            return order != 0 ? order : string.CompareOrdinal(Label, other.Label);
        }
    }
}
