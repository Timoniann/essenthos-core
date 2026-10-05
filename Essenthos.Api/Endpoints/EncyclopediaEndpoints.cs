using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The people, places and peoples the text names, what it calls them, how they stand to one
/// another, and when the source thinks things happened to them.
///
/// A reference here is a canonical verse, not a word. That is what the data states — BibleData
/// tags verses — and stating it at the verse is honest where claiming a word would not be. The
/// word-level layer exists (STEPBible's TIPNR names a person at each occurrence with a
/// disambiguated Strong number) and is a separate load; <c>entity_name.strong_number</c> is the
/// column it will arrive through.
/// </summary>
internal static partial class EncyclopediaEndpoints
{
    private const int MostPerPage = 100;

    /// <summary>
    /// A verse address as one number, so that <em>how many verses</em> is a <c>DISTINCT</c> the
    /// database can do rather than a group the API has to assemble.
    ///
    /// It orders exactly as the three columns order, because no chapter reaches a thousand verses
    /// and no book a thousand chapters — Psalm 119 is 176 verses and Psalms is 150 chapters, and
    /// both stay that way.
    /// </summary>
    private const int ChapterStride = 1_000;

    private const int BookStride = 1_000_000;

    /// <summary>
    /// How many verses name an entity, how many namings they hold, and how many of those verses
    /// carry a naming the source cannot resolve.
    ///
    /// One expression, read by the entity page and by the test that measures it, because the three
    /// numbers differ — 28,226 verses under 30,105 namings — and reporting one of them under
    /// another's name is the whole defect.
    /// </summary>
    internal static readonly Expression<Func<Entity, EntityTally>> Tally =
        e => new EntityTally(
            e.Verses
                .AsQueryable().Where(ShownVerses.IsShown)
                .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                             + v.CanonicalVerse)
                .Distinct().Count(),
            e.Verses.AsQueryable().Count(ShownVerses.IsShown),
            e.Verses
                .AsQueryable().Where(ShownVerses.IsShown).Where(v => v.Disputed)
                .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                             + v.CanonicalVerse)
                .Distinct().Count());

    internal static readonly Expression<Func<Entity, EntitySummaryResponse>> Summary =
        e => new EntitySummaryResponse(
            e.Slug,
            EnumSpelling.Of(e.Kind),
            e.Name,
            e.Distinguisher,
            e.Verses
                .AsQueryable().Where(ShownVerses.IsShown)
                .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                             + v.CanonicalVerse)
                .Distinct().Count(),
            e.Verses.AsQueryable().Count(ShownVerses.IsShown))
        {
            Subtype = e.Subtype,
        };

    /// <summary>
    /// The gazetteer scores an identification in thousandths — 500 high confidence, 1000 very high —
    /// and the API states confidence as a fraction, as it does everywhere else. The scale is not
    /// clamped, because the score is not: it runs past 1000 and below zero, and a clamp would state
    /// something the source did not.
    /// </summary>
    internal const double ScoreScale = 1000.0;

    /// <summary>
    /// The addresses of an entity's verses: those that name it first, then those read as speaking of
    /// it without the name, then those that only concern it, each in canonical order.
    /// </summary>
    internal static IQueryable<int> NamingFirst(IQueryable<EntityVerse> namings) =>
        namings
            .GroupBy(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride) + v.CanonicalVerse)
            .Select(g => new
            {
                Address = g.Key,
                Names = g.Max(v => v.Names ? 1 : 0),
                SpokenOf = g.Max(v => v.Source.EndsWith(ReferenceKinds.SpokenOfMark) ? 1 : 0),
            })
            .OrderBy(a => a.SpokenOf == 1 ? SpokenOfPlace : a.Names == 1 ? NamedPlace : ConcerningPlace)
            .ThenBy(a => a.Address)
            .Select(a => a.Address);

    private const int NamedPlace = 0;

    private const int SpokenOfPlace = 1;

    private const int ConcerningPlace = 2;

    internal static IQueryable<int> Addresses(IQueryable<EntityVerse> namings) =>
        namings
            .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride) + v.CanonicalVerse)
            .Distinct();

    /// <summary>
    /// How far each layer of the encyclopedia actually reaches, asked of the database rather than
    /// declared anywhere.
    ///
    /// A layer whose books stop short of the canon makes every count on every page of it a
    /// statement about the source and not about the text: the place layer states references for
    /// Genesis and Exodus and nothing after them, so Jerusalem reports one verse for a city the
    /// text names hundreds of times. The books are what turns that number from a wrong answer into
    /// a partial one.
    ///
    /// Four aggregates over the whole table rather than one, because the four questions are
    /// different — how many entities exist, how many are cited at all, how many verses cite them,
    /// and how many citations those verses hold — and a single query answering them together
    /// double-counts every entity named twice in a verse.
    /// </summary>
    internal static async Task<EntityCoverageResponse> Coverage(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        var tallies = await db.Entities
            .GroupBy(e => e.Kind)
            .Select(g => new
            {
                Kind = g.Key,
                Entities = g.Count(),
                Named = g.Count(e => e.Verses.AsQueryable().Any(ShownVerses.IsShown)),
            })
            .ToListAsync(cancellationToken);

        var mentions = await db.EntityVerses.Shown()
            .GroupBy(v => v.Entity!.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(row => row.Kind, row => row.Count, cancellationToken);

        var verses = await db.EntityVerses.Shown()
            .Select(v => new
            {
                v.Entity!.Kind,
                Address = (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride) + v.CanonicalVerse,
            })
            .Distinct()
            .GroupBy(v => v.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(row => row.Kind, row => row.Count, cancellationToken);

        var reached = (await db.EntityVerses.Shown()
                .Select(v => new { v.Entity!.Kind, v.CanonicalBook })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(b => b.Kind)
            .ToDictionary(g => g.Key, g => g.Select(b => b.CanonicalBook).Order().ToList());

        var stated = await Stated(db, cancellationToken);

        return new EntityCoverageResponse(
            BookReferences.CanonBookCount,
            [
                .. tallies
                    .Select(t =>
                    {
                        var ordinals = reached.GetValueOrDefault(t.Kind, []);
                        return new EntityLayerCoverageResponse(
                            EnumSpelling.Of(t.Kind),
                            t.Entities,
                            t.Named,
                            verses.GetValueOrDefault(t.Kind),
                            mentions.GetValueOrDefault(t.Kind),
                            new CoverageResponse(
                                ordinals.Count > 0 ? ordinals[0] : 0,
                                ordinals.Count > 0 ? ordinals[^1] : 0,
                                ordinals),
                            stated.GetValueOrDefault(t.Kind, []));
                    })
                    .OrderBy(layer => layer.Kind, StringComparer.Ordinal),
            ]);
    }

    /// <summary>
    /// Which dataset states each layer's references, and how far each one of them reaches.
    ///
    /// The place layer is two sources over one set of places, and they are not alike: one names
    /// 118 places and stops after Exodus, the other names 1,342 across the canon and joins onto
    /// the same entries wherever the two mean the same place. A layer total that adds them
    /// together is true and says nothing — this is what turns it back into two statements, each
    /// attributable to whoever made it.
    /// </summary>
    private static async Task<Dictionary<EntityKind, List<EntitySourceCoverageResponse>>> Stated(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var mentions = await db.EntityVerses.Shown()
            .GroupBy(v => new { v.Entity!.Kind, v.Source })
            .Select(g => new { g.Key.Kind, g.Key.Source, Mentions = g.Count() })
            .ToListAsync(cancellationToken);

        var verses = (await db.EntityVerses.Shown()
                .Select(v => new
                {
                    v.Entity!.Kind,
                    v.Source,
                    Address = (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                              + v.CanonicalVerse,
                })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(v => (v.Kind, v.Source))
            .ToDictionary(g => g.Key, g => g.Count());

        var books = (await db.EntityVerses.Shown()
                .Select(v => new { v.Entity!.Kind, v.Source, v.CanonicalBook })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(b => (b.Kind, b.Source))
            .ToDictionary(g => g.Key, g => g.Select(b => b.CanonicalBook).Order().ToList());

        return mentions
            .GroupBy(m => m.Kind)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(m => m.Mentions)
                    .Select(m =>
                    {
                        var ordinals = books.GetValueOrDefault((m.Kind, m.Source), []);
                        return new EntitySourceCoverageResponse(
                            Datasets.Of(m.Source),
                            m.Source,
                            verses.GetValueOrDefault((m.Kind, m.Source)),
                            m.Mentions,
                            new CoverageResponse(
                                ordinals.Count > 0 ? ordinals[0] : 0,
                                ordinals.Count > 0 ? ordinals[^1] : 0,
                                ordinals));
                    })
                    .ToList());
    }


    /// <summary>
    /// How the index is ordered. Alphabetical is the default and the only order that answers
    /// <em>where is the name I am looking for</em>; the two counts answer <em>who does this corpus
    /// have most to say about</em>, and they are different numbers — 28,226 verses under 30,105
    /// namings — so a client that offers a sort has to name which of them it used.
    ///
    /// It is a query parameter rather than something the client does to a page because the page is
    /// a hundred of four and a half thousand rows: sorting what arrived would order the first
    /// hundred alphabetical names by verse count and present that as the corpus's own ranking.
    ///
    /// Both counts descend, because the question they answer only has a useful end at the top, and
    /// both break ties on the name so that a page boundary falls in the same place twice. The name
    /// is the one shown, in the language asked for and in that language's own alphabetical order.
    /// </summary>
    internal static IQueryable<LocalisedEntity> Ordered(
        AppDbContext db,
        IQueryable<LocalisedEntity> entities,
        string? sort,
        string? q,
        string? language) => sort switch
    {
        "verses" => EntityNames.Alphabetical(
            entities.OrderByDescending(l => l.Entity.Verses
                .AsQueryable().Where(ShownVerses.IsShown)
                .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                             + v.CanonicalVerse)
                .Distinct().Count()),
            language),
        "mentions" => EntityNames.Alphabetical(
            entities.OrderByDescending(l => l.Entity.Verses.AsQueryable().Count(ShownVerses.IsShown)),
            language),
        "name" => EntityNames.Alphabetical(entities, language),
        _ => q is { Length: > 0 }
            ? EntityNames.ByRelevance(db, entities, q, language)
            : EntityNames.Alphabetical(entities, language),
    };

    /// <summary>The orders <c>sort</c> accepts. Anything else is refused rather than ignored.</summary>
    private static readonly string[] Sorts = ["name", "verses", "mentions"];

    /// <summary>
    /// Whatever opens a name before its first letter — a quotation mark, an apostrophe, a digit.
    /// None of the loaded names has any today; the day one does, it is filed under its first
    /// letter rather than under a punctuation mark no index button offers.
    /// </summary>
    private const string BeforeTheFirstLetter = "^[^[:alpha:]]*";

    /// <summary>
    /// The letter a name is filed under in the A to Z index: its first letter, in capitals, and the
    /// empty string for a name with no letter in it at all. The printed name as it stands — <em>the
    /// angel of the LORD</em> files under T, where a reader scanning a list of printed names will
    /// look for it — and it is the name shown in the language asked for, so <em>Аарон</em> files under
    /// А.
    ///
    /// <para>
    /// The counts are read with this and the filter with <see cref="UnderLetter"/>, which says the
    /// same thing to Postgres as a regular expression; the provider translates no regex that
    /// returns a string, so the two are written twice and a test holds them to each other.
    /// </para>
    /// </summary>
    internal static string FiledUnder(string name)
    {
        foreach (var c in name)
        {
            if (char.IsLetter(c))
            {
                return char.ToUpperInvariant(c).ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>The entities filed under one letter, whatever case it was asked in.</summary>
    internal static IQueryable<LocalisedEntity> UnderLetter(IQueryable<LocalisedEntity> entities, char letter)
    {
        var pattern = $"{BeforeTheFirstLetter}[{char.ToUpperInvariant(letter)}{char.ToLowerInvariant(letter)}]";
        return entities.Where(l => Regex.IsMatch(l.Shown, pattern));
    }

    /// <summary>
    /// How many entities each letter holds: every letter of the language's alphabet, empty ones at
    /// zero, and any other letter a name happens to open with after them — in a Ukrainian index, the
    /// Latin initials of the names the corpus holds no Ukrainian name for. The alphabet is in the
    /// response so a client can draw all of it, the empty letters greyed, without holding its own.
    ///
    /// Counted here rather than grouped in the database, because the grouping key is a string the
    /// provider cannot compute. It reads one column of five thousand short rows.
    /// </summary>
    internal static async Task<EntityLettersResponse> Letters(
        IQueryable<LocalisedEntity> entities,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var alphabet = EntityNames.Alphabet(language);
        var counted = (await entities.Select(l => l.Shown).ToListAsync(cancellationToken))
            .GroupBy(FiledUnder)
            .ToDictionary(g => g.Key, g => g.Count());

        var letters = alphabet.Select(c => c.ToString())
            .Concat(counted.Keys
                .Where(letter => letter.Length > 0 && !alphabet.Contains(letter, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal))
            .Select(letter => new EntityLetterResponse(letter, counted.GetValueOrDefault(letter)))
            .ToList();

        return new EntityLettersResponse(letters.Sum(l => l.Count), letters);
    }

    /// <summary>
    /// The entities of one kind, or a refusal naming the kinds there are. Shared by the index and
    /// its letter counts, so the two accept exactly the same words.
    /// </summary>
    private static bool OfKind(ref IQueryable<Entity> entities, string? kind, out IResult? refusal)
    {
        refusal = null;
        if (kind is not { Length: > 0 })
        {
            return true;
        }

        if (kind is not ("person" or "place" or "people" or "term" or "title" or "object" or "observance"))
        {
            refusal = Results.BadRequest(new ProblemResponse(
                $"\"{kind}\" is not a kind of entity. Try person, place, people, term, title, object or observance."));
            return false;
        }

        var wanted = EnumSpelling.ToEntityKind(kind);
        entities = entities.Where(e => e.Kind == wanted);
        return true;
    }

    /// <summary>
    /// The one letter <c>letter</c> names, or a refusal saying what it takes. A word or a digit is
    /// refused rather than read as its first character, because an index that quietly answers a
    /// different question from the one asked is harder to notice than one that says no.
    /// </summary>
    private static bool OneLetter(string letter, out char wanted, out IResult? refusal)
    {
        wanted = default;
        refusal = null;

        if (letter.Length != 1 || !char.IsLetter(letter[0]))
        {
            refusal = Results.BadRequest(new ProblemResponse(
                $"\"{letter}\" is not one letter. Ask for a single letter, such as letter=A."));
            return false;
        }

        wanted = letter[0];
        return true;
    }

    /// <summary>A passage as the wire carries it: the book once, and the span in its own numbers.</summary>
    internal static PassageResponse Passage(EntityPassage passage) =>
        new(
            passage.Role,
            new BookRefResponse(
                passage.CanonicalBook,
                BookReferences.Name(passage.CanonicalBook),
                BookReferences.Slug(passage.CanonicalBook)),
            passage.CanonicalChapter,
            passage.CanonicalVerse,
            passage.EndChapter,
            passage.EndVerse,
            passage.Note);

    public static void MapEncyclopedia(this IEndpointRouteBuilder routes)
    {
        MapEntityIndex(routes);
        MapEntity(routes);
        MapPlaces(routes);
        MapReferences(routes);
        MapEvents(routes);
        MapChronology(routes);
    }
}
