using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The order of the events of one year: the order the text tells them in.
///
/// <para>
/// A year holds a dozen events at times, the Creation and Adam's birth, the end of the Flood and the
/// covenant after it, and the years say nothing of which came first. The text does: an event the source
/// reads from a verse stands at that verse, by book, chapter and verse, so the Creation (Genesis 1:1)
/// comes before Adam's birth (1:27) and the Flood's end (8:13) before the covenant (9:9).
/// </para>
///
/// <list type="number">
/// <item>Scripture's events read from a verse, in the order of their verses. The verse is the event's own
/// reference; failing that, the first verse its description cites; failing that, the verse in
/// <see cref="Placed"/>, read by hand.</item>
/// <item>Ussher's annals, in the order of his paragraphs. He sets the four Gospels in one narrative and
/// says <em>the next day</em> across his paragraphs, so his order is the text's order of those events and
/// their verses (Matthew's, then Luke's, then Matthew's again) are not. No year holds annals beside
/// another source's events.</item>
/// <item>Scripture's events no verse places (the jubilees, the Persian kings' accessions, the ends of
/// prophets' times): the text does not put them before anything it tells, so they follow it.</item>
/// <item>The world's events, which are in no verse at all.</item>
/// </list>
///
/// <para>
/// At one verse a birth comes first, then a death, the end of a reign or a time, the beginning of the
/// next, and then the rest: <em>Rehoboam died</em>, <em>his reign ended</em>, <em>Abijam's began</em>.
/// A reign that begins and ends at the same verse (Zimri's seven days) begins first. A reign or a time that
/// ends in the year its person dies ends at the death, after it: the source reads such an end from the verse
/// that counts the reign (Ahab's from 1 Kings 16:29, where he begins), which would put it before the battle
/// he dies in. Where the text shows the reign ended before the death, by a handover or otherwise, the end stands where
/// it ended instead (<see cref="EndedElsewhere"/>). <see cref="Later"/> holds the few events that follow everything else
/// told at their verse.
/// </para>
///
/// <para>
/// Both rules that read another event, the end at the death and the end right after its own beginning, read it among
/// all the events, so <see cref="Rank"/> is one order over the whole corpus and a narrowed list keeps the order the
/// whole one has: hiding the deaths does not move a reign's end.
/// </para>
/// </summary>
internal static class EventOrder
{
    /// <summary>What an event's place in its year is read from; the description only where the event has no reference.</summary>
    internal sealed record Facts(
        string Slug,
        string Realm,
        string? Source,
        string? Kind,
        int? Book,
        int? Chapter,
        int? Verse,
        string? Description,
        int? SequenceInYear,
        int? YearFromCreation,
        int? EntityId = null);

    /// <summary>An event's place in its year; compare two with <see cref="InYear"/>.</summary>
    internal readonly record struct Place(int Part, int Book, int Chapter, int Verse, int Moment, string Span, int SpanEnd, int? Sequence, string Slug);

    /// <summary>
    /// BibleData's events with neither a reference nor a citation that a verse plainly tells of, each at that
    /// verse (BibleData's book codes).
    /// </summary>
    private static readonly Dictionary<string, string> Placed = new(StringComparer.Ordinal)
    {
        // "The beginning of the period of time Israel was governed by the judges": "the LORD raised up judges".
        ["begineraofthejudges"] = "JDG 2:16",
        // "This reconciles 2KI 1:17 and 2KI 3:1": 1:17 dates Jehoram of Israel by "the second year of Jehoram son of Jehoshaphat".
        ["beginjehoram1regency"] = "2KI 1:17",
        // "His regency ended when he becomes king": Jehoram begins to reign over Judah.
        ["endjehoram1regency"] = "2KI 8:16",
        // "Began with Azariah's death and the end of his reign": "Azariah slept with his fathers … Jotham his son reigned".
        ["beginjotham2solereign"] = "2KI 15:7",
        ["endjotham2coregencywithazariah"] = "2KI 15:7",
        // "Ahaz's co-regency with his father ended with his father's death": "Jotham slept with his fathers … Ahaz his son reigned".
        ["beginahaz1reign"] = "2KI 15:38",
        ["endahaz1coregencywithjotham"] = "2KI 15:38",
        // "Ended with the death of Menachem": "Menahem slept with his fathers; and Pekahiah his son reigned".
        ["endpekah1rivalreignwmenachem"] = "2KI 15:22",
        // "Around the time Jeroboam's reign ended": "Jeroboam slept with his fathers".
        ["endjonah1prophesying"] = "2KI 14:29",
        // "Ended with his defeat by Cyrus": "In that night was Belshazzar the king of the Chaldeans slain".
        ["endbelshazzar1reign"] = "DAN 5:30",
    };

    /// <summary>
    /// Reigns that ended where the text says, not at the death the year of the end would put them at, each at the verse
    /// that tells the end (BibleData's book codes).
    /// </summary>
    private static readonly Dictionary<string, string> EndedElsewhere = new(StringComparer.Ordinal)
    {
        // David handed the kingdom over while he lived: "they anointed Solomon … God save king Solomon" (1 Kings 1:39;
        // "he made Solomon his son king over Israel", 1 Chronicles 23:1), before "David slept with his fathers" (2:10).
        ["enddavid1reign"] = "1KI 1:39",
        ["enddavidreignjerusalem"] = "1KI 1:39",
        // Jehoram of Judah ended at his death: "Joram slept with his fathers … and Ahaziah his son reigned in his stead"
        // (2 Kings 8:24). The death row filed under him quotes 9:24, Jehu's arrow in Joram king of Israel (9:21).
        ["endjehoram1reign"] = "2KI 8:24",
    };

    /// <summary>Events that follow everything else told at their verse.</summary>
    private static readonly HashSet<string> Later = new(StringComparer.Ordinal)
    {
        // The 430 years are counted from the promise told at Genesis 15:1.
        "begin430yearspromisetocovenant",
        // Jehu's time as prophet ends with the prophecy to Jehoshaphat told at 2 Chronicles 19:2.
        "endjehu1prophesying",
        // The Philistines hold the ark from its capture, told at 1 Samuel 4:11.
        "beginphilistineshavetheark",
        "endphilistineshavetheark",
        // Manasseh is the firstborn (Genesis 41:51-52); the source puts both births at 41:50.
        "birthephraim1",
    };

    private const int ByVerse = 0;
    private const int ByParagraph = 1;
    private const int Unplaced = 2;
    private const int World = 3;

    private const string Begin = "begin";
    private const string End = "end";

    /// <summary>The place of each of these events in its year, keyed by slug.</summary>
    public static Dictionary<string, Place> Of(IReadOnlyCollection<Facts> events)
    {
        var verses = events.ToDictionary(e => e.Slug, Verse, StringComparer.Ordinal);
        var deaths = events
            .Where(e => e.Kind == "Death" && e.EntityId is not null && verses[e.Slug] is not null)
            .GroupBy(e => (e.EntityId, e.YearFromCreation))
            .ToDictionary(g => g.Key, g => verses[g.First().Slug]);
        foreach (var e in events.Where(e => e.Kind == "End" && e.EntityId is not null && !EndedElsewhere.ContainsKey(e.Slug)))
        {
            if (deaths.TryGetValue((e.EntityId, e.YearFromCreation), out var died))
            {
                verses[e.Slug] = died;
            }
        }

        var opened = events
            .Where(e => e.Kind == "Begin" && e.Slug.StartsWith(Begin, StringComparison.Ordinal) && verses[e.Slug] is not null)
            .Select(e => (verses[e.Slug]!.Value, e.Slug[Begin.Length..]))
            .ToHashSet();

        var places = new Dictionary<string, Place>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            var verse = verses[e.Slug];
            var part = e.Realm == Realms.World ? World
                : Datasets.Of(e.Source) == UssherDataset ? ByParagraph
                : verse is null ? Unplaced
                : ByVerse;
            var (book, chapter, number) = part == ByVerse ? verse!.Value : (0, 0, 0);

            var span = "";
            var spanEnd = 0;
            var moment = Moment(e.Kind);
            if (e.Kind == "Begin" && e.Slug.StartsWith(Begin, StringComparison.Ordinal))
            {
                span = e.Slug[Begin.Length..];
            }
            else if (e.Kind == "End" && e.Slug.StartsWith(End, StringComparison.Ordinal) && verse is { } at
                     && opened.Contains((at, e.Slug[End.Length..])))
            {
                span = e.Slug[End.Length..];
                spanEnd = 1;
                moment = Moment("Begin");
            }

            if (Later.Contains(e.Slug))
            {
                moment = LastMoment;
            }

            places[e.Slug] = new Place(part, book, chapter, number, moment, span, spanEnd, e.SequenceInYear, e.Slug);
        }

        return places;
    }

    /// <summary>Each event's rank among these in the text's order, from 0; ranks of two events compare as <see cref="InYear"/> does.</summary>
    public static Dictionary<string, int> Rank(IReadOnlyCollection<Facts> events)
    {
        var places = Of(events);
        return places.Values.Order(InYear).Select((place, rank) => (place.Slug, rank))
            .ToDictionary(p => p.Slug, p => p.rank, StringComparer.Ordinal);
    }

    /// <summary>Two events of one year in the text's order.</summary>
    public static readonly IComparer<Place> InYear = Comparer<Place>.Create((x, y) =>
    {
        var by = x.Part.CompareTo(y.Part);
        if (by == 0 && x.Part == ByVerse)
        {
            by = (x.Book, x.Chapter, x.Verse).CompareTo((y.Book, y.Chapter, y.Verse));
        }

        if (by == 0 && x.Part is ByVerse or Unplaced)
        {
            by = x.Moment.CompareTo(y.Moment);
            by = by != 0 ? by : string.CompareOrdinal(x.Span, y.Span);
            by = by != 0 ? by : x.SpanEnd.CompareTo(y.SpanEnd);
        }

        if (by == 0)
        {
            by = (x.Sequence is null).CompareTo(y.Sequence is null);
            by = by != 0 ? by : Nullable.Compare(x.Sequence, y.Sequence);
        }

        return by != 0 ? by : EncyclopediaEndpoints.NumberAwareOrder.Instance.Compare(x.Slug, y.Slug);
    });

    private const string UssherDataset = "ussher";

    private const int LastMoment = 5;

    /// <summary>Where at one verse an event of this kind stands: births, deaths, ends, beginnings, then the rest.</summary>
    private static int Moment(string? kind) => kind switch
    {
        "Birth" => 0,
        "Death" => 1,
        "End" => 2,
        "Begin" => 3,
        _ => 4,
    };

    /// <summary>
    /// The verse an event is read from: where the text ends it (<see cref="EndedElsewhere"/>), its reference, the first
    /// verse its description cites, or <see cref="Placed"/>.
    /// </summary>
    private static (int Book, int Chapter, int Verse)? Verse(Facts e)
    {
        if (EndedElsewhere.TryGetValue(e.Slug, out var ended) && QuotedVerses.Passages(ended) is [var end, ..])
        {
            return (end.Book, end.Chapter, end.Verse);
        }

        if (e.Book is { } book && e.Chapter is { } chapter && e.Verse is { } verse)
        {
            return (book, chapter, verse);
        }

        if (QuotedVerses.FirstCited(e.Slug, e.Description) is { } cited)
        {
            return cited;
        }

        return Placed.TryGetValue(e.Slug, out var written) && QuotedVerses.Passages(written) is [var first, ..]
            ? (first.Book, first.Chapter, first.Verse)
            : null;
    }
}
