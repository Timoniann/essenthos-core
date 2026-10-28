using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record OccupationName(string Name, string Masculine, string Feminine, string? Description = null);

internal sealed record OccupationConcept(
    string Slug, string Category, IReadOnlyList<string> StrongNumbers, Dictionary<string, OccupationName> Labels);

/// <param name="Reference">The verse as <paramref name="Text"/> numbers it, <c>NEH 10:3</c>.</param>
/// <param name="Quote">The words as that text prints them in that verse, letter for letter.</param>
internal sealed record OccupationQuote(string Kind, string Text, string Reference, string Quote);

/// <param name="Source">Who read it and who decided it; the file's own where null.</param>
internal sealed record OccupationClaim(string Person, string Occupation, IReadOnlyList<OccupationQuote> Witnesses, string? Source = null);

internal sealed record OccupationFile(
    int Version, string Source, IReadOnlyList<OccupationConcept> Occupations, IReadOnlyList<OccupationClaim> Assertions);

internal sealed record OccupationLoadOutcome(int Written, int Removed, int Total, int WordsWritten, int WordsRemoved)
{
    public override string ToString() =>
        $"occupations: {Written} written, {Removed} removed, {Total} held; their words: {WordsWritten} marked, {WordsRemoved} taken back";
}

/// <summary>
/// What the text says people did or were, each with the verses that say it, from
/// <c>Essenthos/person-occupations.json</c>; and the words of those verses that name it.
///
/// <para>
/// Every quotation is checked against the corpus itself: the verse is found in the text it names, as
/// that text numbers it, and the quotation must stand in it letter for letter. One that does not stops
/// the load and names every such quotation, as a ruling whose word is not at its address does. The
/// verse's place in the shared frame is the one the corpus gives it, so BHSA's <c>NEH 10:3</c> is
/// cited as the English 10:2.
/// </para>
///
/// <para>
/// The words marked are those of a quotation that carry one of the occupation's Strong numbers, and
/// in every other text the words the links say render one of them: a word of a Renders or Equals
/// link that carries the number itself, or that is the only word on its side of a link whose other
/// side is that word alone. A link that renders <em>the king</em> by <em>So the king</em> says which
/// words go together, not which of them is the king: there the word marked is the one carrying the
/// number, or the one the text most often renders the number by on its own (<em>king</em>). A pronoun
/// is never marked, and a word common in its text (<em>was</em>, <em>under</em>) only where the text
/// renders the number by it often.
/// </para>
/// </summary>
internal sealed partial class OccupationLoader(AppDbContext db)
{
    public const string FileName = "person-occupations.json";
    internal static readonly string[] Languages = ["eng", "ukr", "deu", "spa"];

    internal static readonly string[] Categories =
        ["trade", "office", "military_role", "religious_role", "heavenly_role", "designation"];

    private static readonly JsonSerializerOptions Shape = new(JsonSerializerDefaults.Web);

    public async Task<OccupationLoadOutcome> Load(string resources, CancellationToken cancellationToken)
    {
        var path = Path.Combine(resources, "Essenthos", FileName);
        // An installation without this optional dataset does not erase an already loaded one.
        if (!File.Exists(path))
        {
            return new(0, 0, await db.PersonOccupations.CountAsync(cancellationToken), 0, 0);
        }

        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<OccupationFile>(stream, Shape, cancellationToken)
                   ?? throw new InvalidDataException($"{path} is empty.");
        return await Load(file, cancellationToken);
    }

    internal static void Validate(OccupationFile file)
    {
        if (file.Version != 1 || string.IsNullOrWhiteSpace(file.Source) || file.Occupations.Count == 0)
        {
            throw new InvalidDataException("Occupations need version 1, a source and at least one occupation.");
        }

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concept in file.Occupations)
        {
            if (!SlugShape().IsMatch(concept.Slug) || !slugs.Add(concept.Slug) || !Categories.Contains(concept.Category)
                || concept.StrongNumbers.Any(number => StrongNumbers.Normalize(number) != number)
                || concept.Labels.Count != Languages.Length
                || Languages.Any(language => !concept.Labels.TryGetValue(language, out var label)
                                             || string.IsNullOrWhiteSpace(label.Name)
                                             || string.IsNullOrWhiteSpace(label.Masculine)
                                             || string.IsNullOrWhiteSpace(label.Feminine)))
            {
                throw new InvalidDataException(
                    $"Occupation {concept.Slug} needs a unique slug, a known category, plain Strong numbers and its name in {string.Join(", ", Languages)}.");
            }
        }

        var pairs = new HashSet<(string, string)>();
        foreach (var claim in file.Assertions)
        {
            if (!slugs.Contains(claim.Occupation) || !pairs.Add((claim.Person, claim.Occupation))
                || !claim.Witnesses.Any(w => w.Kind == Profession)
                || claim.Witnesses.Any(w => w.Kind is not (Profession or Identity) || string.IsNullOrWhiteSpace(w.Quote)
                                            || Address(w.Reference) is null))
            {
                throw new InvalidDataException(
                    $"{claim.Person} as {claim.Occupation}: each person is said once per occupation, by at least one quotation saying it, each at a verse.");
            }
        }
    }

    internal async Task<OccupationLoadOutcome> Load(OccupationFile file, CancellationToken cancellationToken = default)
    {
        Validate(file);
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var people = await People(file, cancellationToken);
            var quoted = await Find(file, cancellationToken);
            var concepts = await Concepts(file, cancellationToken);

            var held = await db.PersonOccupations.Include(p => p.Witnesses)
                .ToDictionaryAsync(p => (p.PersonId, p.OccupationId), cancellationToken);
            var wanted = file.Assertions
                .Select(claim => (Key: (people[claim.Person], concepts[claim.Occupation].Id), Claim: claim))
                .ToList();
            var written = 0;
            foreach (var (key, claim) in wanted)
            {
                var source = claim.Source ?? file.Source;
                var witnesses = claim.Witnesses.Distinct().Select(w => Witness(w, quoted[w])).ToList();
                if (held.Remove(key, out var row))
                {
                    var same = row.Source == source && row.Witnesses.Count == witnesses.Count
                               && row.Witnesses.Select(Fingerprint).Order().SequenceEqual(witnesses.Select(Fingerprint).Order());
                    if (same)
                    {
                        continue;
                    }

                    row.Source = source;
                    db.OccupationWitnesses.RemoveRange(row.Witnesses);
                    row.Witnesses = witnesses;
                }
                else
                {
                    db.PersonOccupations.Add(new PersonOccupation
                    {
                        PersonId = key.Item1, OccupationId = key.Item2, Source = source, Witnesses = witnesses,
                    });
                }

                written++;
            }

            db.PersonOccupations.RemoveRange(held.Values);
            await db.SaveChangesAsync(cancellationToken);

            var (marked, unmarked) = await MarkTheWords(file, people, concepts, quoted, cancellationToken);
            if (own is not null) await own.CommitAsync(cancellationToken);
            return new(written, held.Count, wanted.Count, marked, unmarked);
        }
        catch
        {
            if (own is not null) await own.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (own is not null) await own.DisposeAsync();
        }
    }

    private const string Profession = "profession";
    private const string Identity = "identity";

    private static string Fingerprint(OccupationWitness w) =>
        $"{w.Kind}|{w.Text}|{w.Reference}|{w.CanonicalBook}|{w.CanonicalChapter}|{w.CanonicalVerse}|{w.Quote}";

    private static OccupationWitness Witness(OccupationQuote quote, Quoted found) => new()
    {
        Kind = quote.Kind, Text = quote.Text, Reference = quote.Reference, Quote = quote.Quote,
        CanonicalBook = found.Canonical.Book, CanonicalChapter = found.Canonical.Chapter, CanonicalVerse = found.Canonical.Verse,
    };

    /// <summary>The people by the slug the file names them by, a folded record's slug reaching the kept one.</summary>
    private async Task<Dictionary<string, int>> People(OccupationFile file, CancellationToken cancellationToken)
    {
        var slugs = file.Assertions.Select(a => a.Person).Distinct().ToList();
        var people = await db.Entities.Where(e => slugs.Contains(e.Slug) && e.Kind == EntityKind.Person)
            .ToDictionaryAsync(e => e.Slug, e => e.Id, cancellationToken);
        var folded = await db.MergedRecords.Where(m => slugs.Contains(m.Slug) && m.Entity!.Kind == EntityKind.Person)
            .Select(m => new { m.Slug, m.EntityId }).ToListAsync(cancellationToken);
        foreach (var alias in folded)
        {
            people.TryAdd(alias.Slug, alias.EntityId);
        }

        var unknown = slugs.Where(slug => !people.ContainsKey(slug)).ToList();
        return unknown.Count == 0
            ? people
            : throw new InvalidDataException($"Occupations name {unknown.Count} people the encyclopedia does not hold: {string.Join(", ", unknown.Take(20))}.");
    }

    /// <summary>The occupations written as the file says them, those it no longer names removed.</summary>
    private async Task<Dictionary<string, Occupation>> Concepts(OccupationFile file, CancellationToken cancellationToken)
    {
        var held = await db.Occupations.Include(o => o.Labels).ToDictionaryAsync(o => o.Slug, cancellationToken);
        foreach (var concept in file.Occupations)
        {
            if (!held.TryGetValue(concept.Slug, out var occupation))
            {
                occupation = new Occupation { Slug = concept.Slug, Category = concept.Category };
                db.Occupations.Add(occupation);
                held[concept.Slug] = occupation;
            }

            occupation.Category = concept.Category;
            if (!occupation.StrongNumbers.SequenceEqual(concept.StrongNumbers))
            {
                occupation.StrongNumbers = [.. concept.StrongNumbers];
            }

            foreach (var (language, name) in concept.Labels)
            {
                var label = occupation.Labels.SingleOrDefault(l => l.Language == language);
                if (label is null)
                {
                    label = new OccupationLabel { Language = language, Name = name.Name, Masculine = name.Masculine, Feminine = name.Feminine };
                    occupation.Labels.Add(label);
                }

                label.Name = name.Name;
                label.Masculine = name.Masculine;
                label.Feminine = name.Feminine;
                label.Description = name.Description;
            }
        }

        var named = file.Occupations.Select(o => o.Slug).ToHashSet(StringComparer.Ordinal);
        db.Occupations.RemoveRange(held.Values.Where(o => !named.Contains(o.Slug)));
        await db.SaveChangesAsync(cancellationToken);
        return held.Where(pair => named.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private sealed record QuotedWord(long Id, string? StrongNumber);

    /// <summary>Where a quotation stands: its verse's place in the frame and the words it spans.</summary>
    private sealed record Quoted((int Book, int Chapter, int Verse) Canonical, IReadOnlyList<QuotedWord> Words);

    /// <summary>
    /// Every quotation found in the corpus: its verse in the text it names, numbered as that text
    /// numbers it, and the words its letters fall on. A quotation that is not there stops the load.
    /// </summary>
    private async Task<Dictionary<OccupationQuote, Quoted>> Find(OccupationFile file, CancellationToken cancellationToken)
    {
        var quotes = file.Assertions.SelectMany(a => a.Witnesses).Distinct().ToList();
        var slugs = quotes.Select(q => q.Text).Distinct().ToList();
        var texts = await db.Texts.Where(t => slugs.Contains(t.Slug)).ToDictionaryAsync(t => t.Slug, t => t.Id, cancellationToken);
        var keys = quotes.Select(q => Address(q.Reference)!.Value).Select(Key).Distinct().ToList();
        var textIds = texts.Values.ToList();
        var verses = (await db.Verses
                .Where(v => textIds.Contains(v.TextId)
                            && keys.Contains((v.Book!.CanonicalOrdinal * 1_000_000) + (v.ChapterNumber * 1_000) + v.Number))
                .Select(v => new
                {
                    v.Id, v.TextId, v.Label,
                    Key = (v.Book!.CanonicalOrdinal * 1_000_000) + (v.ChapterNumber * 1_000) + v.Number,
                })
                .ToListAsync(cancellationToken))
            .ToLookup(v => (v.TextId, v.Key));
        var verseIds = verses.SelectMany(group => group.Select(v => v.Id)).ToList();
        var words = (await db.Words.Where(w => verseIds.Contains(w.VerseId))
                .Select(w => new { w.VerseId, w.Id, w.Position, w.Surface, w.Trailer, w.StrongNumber })
                .ToListAsync(cancellationToken))
            .GroupBy(w => w.VerseId)
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.Position).ToList());
        var placed = (await db.VerseReferences.Where(r => verseIds.Contains(r.VerseId))
                .Select(r => new { r.VerseId, r.IsPrimary, r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse })
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.VerseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.IsPrimary).ThenBy(r => r.CanonicalChapter).ThenBy(r => r.CanonicalVerse).First());

        var found = new Dictionary<OccupationQuote, Quoted>();
        var missing = new List<string>();
        foreach (var quote in quotes)
        {
            var key = Key(Address(quote.Reference)!.Value);
            Quoted? hit = null;
            if (texts.TryGetValue(quote.Text, out var textId))
            {
                foreach (var verse in verses[(textId, key)].OrderBy(v => v.Label, StringComparer.Ordinal))
                {
                    var spelled = words.GetValueOrDefault(verse.Id) ?? [];
                    var line = string.Concat(spelled.Select(w => w.Surface + w.Trailer));
                    var at = line.IndexOf(quote.Quote, StringComparison.Ordinal);
                    if (at < 0 || !placed.TryGetValue(verse.Id, out var place))
                    {
                        continue;
                    }

                    var end = at + quote.Quote.Length;
                    var spanned = new List<QuotedWord>();
                    var offset = 0;
                    foreach (var word in spelled)
                    {
                        if (offset < end && offset + word.Surface.Length > at)
                        {
                            spanned.Add(new QuotedWord(word.Id, word.StrongNumber));
                        }

                        offset += word.Surface.Length + word.Trailer.Length;
                    }

                    hit = new Quoted((place.CanonicalBook, place.CanonicalChapter, place.CanonicalVerse), spanned);
                    break;
                }
            }

            if (hit is null)
            {
                missing.Add($"{quote.Text} {quote.Reference} «{quote.Quote}»");
            }
            else
            {
                found[quote] = hit;
            }
        }

        return missing.Count == 0
            ? found
            : throw new InvalidDataException(
                $"{missing.Count} occupation quotations are not in their verse: {string.Join("; ", missing.Take(20))}.");
    }

    /// <summary>
    /// The words that name each occupation in the verses that say it, written where they differ from
    /// what is held: the quotation's words carrying one of its numbers, and the words of other texts
    /// the links say render them.
    /// </summary>
    private async Task<(int Marked, int Unmarked)> MarkTheWords(
        OccupationFile file,
        Dictionary<string, int> people,
        Dictionary<string, Occupation> concepts,
        Dictionary<OccupationQuote, Quoted> quoted,
        CancellationToken cancellationToken)
    {
        var rows = await db.PersonOccupations.Select(p => new { p.Id, p.PersonId, p.OccupationId })
            .ToDictionaryAsync(p => (p.PersonId, p.OccupationId), p => p.Id, cancellationToken);
        var naming = new Dictionary<long, HashSet<int>>();
        var numbersOf = new Dictionary<int, HashSet<string>>();
        foreach (var claim in file.Assertions)
        {
            var concept = concepts[claim.Occupation];
            if (concept.StrongNumbers.Length == 0)
            {
                continue;
            }

            var row = rows[(people[claim.Person], concept.Id)];
            numbersOf[row] = [.. concept.StrongNumbers];
            foreach (var word in claim.Witnesses.Where(w => w.Kind == Profession).SelectMany(w => quoted[w].Words)
                         .Where(w => StrongNumbers.Normalize(w.StrongNumber) is { } number && concept.StrongNumbers.Contains(number)))
            {
                (naming.TryGetValue(word.Id, out var rowsOf) ? rowsOf : naming[word.Id] = []).Add(row);
            }
        }

        var wanted = naming.SelectMany(pair => pair.Value.Select(row => (Row: row, Word: pair.Key))).ToHashSet();
        var seeds = naming.Keys.ToList();
        var sides = await db.LinkWords
            .Where(side => seeds.Contains(side.WordId)
                           && (side.Link!.Relation == LinkRelation.Renders || side.Link.Relation == LinkRelation.Equals))
            .Select(side => new { side.LinkId, side.WordId, side.Side })
            .ToListAsync(cancellationToken);
        var linkIds = sides.Select(side => side.LinkId).Distinct().ToList();
        var members = (await db.LinkWords.Where(other => linkIds.Contains(other.LinkId))
                .Select(other => new
                {
                    other.LinkId, other.WordId, other.Side, other.Word!.StrongNumber, other.Word.TextId,
                    Form = other.Word.NormalisedText,
                })
                .ToListAsync(cancellationToken))
            .ToLookup(other => other.LinkId);
        var candidates = new List<Candidate>();
        foreach (var side in sides)
        {
            var link = members[side.LinkId].ToList();
            var across = link.Where(other => other.Side != side.Side).ToList();
            var single = across.Count == 1 && link.Count(other => other.Side == side.Side) == 1;
            foreach (var row in naming[side.WordId])
            {
                foreach (var other in across)
                {
                    if (StrongNumbers.Normalize(other.StrongNumber) is { } number && numbersOf[row].Contains(number))
                    {
                        wanted.Add((row, other.WordId));
                    }
                    else
                    {
                        candidates.Add(new Candidate(row, other.WordId, other.TextId, other.Form, single));
                    }
                }
            }
        }

        foreach (var candidate in await Saying(candidates, numbersOf, cancellationToken))
        {
            wanted.Add((candidate.Row, candidate.Word));
        }

        var held = (await db.OccupationWords.Select(w => new { w.Id, w.PersonOccupationId, w.WordId }).ToListAsync(cancellationToken))
            .ToDictionary(w => (w.PersonOccupationId, w.WordId), w => w.Id);
        var gone = held.Where(pair => !wanted.Contains(pair.Key)).Select(pair => pair.Value).ToList();
        var fresh = wanted.Where(pair => !held.ContainsKey(pair)).ToList();
        if (gone.Count > 0)
        {
            await db.OccupationWords.Where(w => gone.Contains(w.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        db.OccupationWords.AddRange(fresh.Select(pair => new OccupationWord { PersonOccupationId = pair.Row, WordId = pair.Word }));
        await db.SaveChangesAsync(cancellationToken);
        return (fresh.Count, gone.Count);
    }

    /// <summary>A word as common as this in its text is a function word unless the text renders the number by it often.</summary>
    private const int Common = 300;

    /// <summary>How often a text must render a number by a common word to keep it.</summary>
    private const int Often = 10;

    /// <summary>How many of a number's commonest renderings in a text count as its words there.</summary>
    private const int Commonest = 3;

    /// <summary>A word of another text a link gives with an occupation's word, and whether it is the link's only word.</summary>
    private sealed record Candidate(int Row, long Word, int Text, string? Form, bool Alone);

    /// <summary>
    /// The words of other texts that say the occupation, among those the links give with its word. A
    /// pronoun never does. The only word of a link does, unless it is a word common in its text
    /// (<em>was</em>, <em>under</em>), where an aligner lands when it has nothing better; a common
    /// word is kept where it is among the text's commonest renderings of the number, as the commonest
    /// occupations' words are (<em>king</em>, <em>цар</em>). Of several words a link gives together
    /// (<em>the king</em>), the one that is itself among the commonest renderings of the number in
    /// its text does.
    /// </summary>
    private async Task<List<Candidate>> Saying(
        List<Candidate> candidates,
        Dictionary<int, HashSet<string>> numbersOf,
        CancellationToken cancellationToken)
    {
        candidates = [.. candidates.Where(c => c.Form is not null)];
        var frequency = new Dictionary<(int Text, string Form), int>();
        foreach (var text in candidates.GroupBy(c => c.Text))
        {
            var forms = text.Select(c => c.Form!).Distinct().ToList();
            foreach (var count in await db.Words.Where(w => w.TextId == text.Key && forms.Contains(w.NormalisedText!))
                         .GroupBy(w => w.NormalisedText!).Select(g => new { Form = g.Key, Count = g.Count() })
                         .ToListAsync(cancellationToken))
            {
                frequency[(text.Key, count.Form)] = count.Count;
            }
        }

        var numbers = numbersOf.Values.SelectMany(n => n).Distinct().ToList();
        var textIds = candidates.Select(c => c.Text).Distinct().ToList();
        var languages = await db.Texts.Where(t => textIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Language, cancellationToken);
        var uses = new Dictionary<(int Text, string Number, string Phrase), int>();
        foreach (var rendering in await db.StrongRenderings
                     .Where(r => numbers.Contains(r.StrongNumber) && textIds.Contains(r.TextId) && r.Rank <= Commonest && r.Uses >= 2)
                     .Select(r => new { r.TextId, r.StrongNumber, r.Phrase, r.Uses })
                     .ToListAsync(cancellationToken))
        {
            uses[(rendering.TextId, rendering.StrongNumber, rendering.Phrase.ToLowerInvariant())] = rendering.Uses;
        }

        int Rendered(Candidate c) =>
            numbersOf[c.Row].Select(number => uses.GetValueOrDefault((c.Text, number, c.Form!))).DefaultIfEmpty(0).Max();

        return
        [
            .. candidates.Where(c =>
            {
                if (Pronouns.Is(languages.GetValueOrDefault(c.Text) ?? "", c.Form!))
                {
                    return false;
                }

                var common = frequency.GetValueOrDefault((c.Text, c.Form!)) >= Common;
                var rendered = Rendered(c);
                return c.Alone ? !common || rendered >= Often : rendered > 0 && (!common || rendered >= Often);
            }),
        ];
    }

    private static int Key((int Book, int Chapter, int Verse) address) =>
        (address.Book * 1_000_000) + (address.Chapter * 1_000) + address.Verse;

    /// <summary><c>NEH 10:3</c> as book, chapter and verse; a psalm's title is its verse 0.</summary>
    internal static (int Book, int Chapter, int Verse)? Address(string? reference)
    {
        var match = reference is null ? null : ReferenceShape().Match(reference);
        return match is { Success: true }
               && BookReferences.ResolveOrdinal(match.Groups[1].Value) is { } book
               && int.TryParse(match.Groups[2].ValueSpan, out var chapter) && chapter > 0
               && int.TryParse(match.Groups[3].ValueSpan, out var verse) && verse >= 0
            ? (book, chapter, verse)
            : null;
    }

    [GeneratedRegex(@"^([1-3]?[A-Z]{2,3}) (\d{1,3}):(\d{1,3})$")]
    private static partial Regex ReferenceShape();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugShape();
}
