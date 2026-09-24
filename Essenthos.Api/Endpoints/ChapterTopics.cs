using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The subjects of Nave's Topical Bible that file verses of one chapter, the ones most particular to
/// it first. On trial: nothing else reads the topics, and whether they stay is the owner's call.
///
/// <para>
/// A topic ranks by how much of the chapter it files times how rare it is in the Bible — the share
/// of the chapter's verses, weighed by the logarithm of how few verses of the whole Bible it files,
/// which is tf-idf with a verse for the document. <em>Firmament</em> files ten verses and
/// <em>God</em> nearly five thousand, and a chapter cited under both is about the first more than
/// the second. A citation of the whole chapter is the index's vaguest claim and counts for less than
/// the verses it names; a topic whose heading the chapter's own English says counts for more.
/// </para>
///
/// <para>
/// Three kinds of topic are not themes. One named after a person the chapter names is that
/// person's entry, and belongs on the person's row. One about a person the chapter names nowhere —
/// <em>Jesus, the Christ</em> in Genesis 1, <em>Satan</em> in Genesis 3 — is how Nave read the
/// chapter, not what it says, and is set apart as that. And Nave's own lists of passages, the
/// quotations, the select readings and the Psalms, are the index's apparatus and not subjects at
/// all, so they are left out.
/// </para>
/// </summary>
internal static class ChapterTopics
{
    /// <summary>What a topic is to the chapter, as <see cref="ContextTopicResponse.Group"/> spells it.</summary>
    internal static class Groups
    {
        public const string Theme = "theme";

        public const string Person = "person";

        public const string Reading = "reading";
    }

    /// <summary>Nave's lists of passages, filed as topics but about nothing a chapter says.</summary>
    internal static readonly IReadOnlySet<string> IndexLists =
        new HashSet<string>(StringComparer.Ordinal) { "quotationsandallusions", "readingsselect", "psalms" };

    /// <summary>What a verse cited only as part of the whole chapter counts for where the words say the topic.</summary>
    internal const double WholeChapterSaid = 0.5;

    /// <summary>What a verse cited only as part of the whole chapter counts for where they do not.</summary>
    internal const double WholeChapterUnsaid = 0.25;

    /// <summary>How much more a topic counts when the chapter's own English says its heading.</summary>
    internal const double SaidBonus = 1.5;

    /// <summary>The language a heading is looked for in, since Nave wrote in it.</summary>
    private const string English = "eng";

    internal static async Task<IList<ContextTopicResponse>> InChapter(
        AppDbContext db,
        int book,
        int chapter,
        ContextWeights weights,
        IReadOnlyList<ContextEntityResponse> entities,
        CancellationToken cancellationToken)
    {
        var rows = await db.TopicReferences
            .Where(r => r.CanonicalBook == book && r.CanonicalChapter == chapter)
            .Select(r => new { r.Topic!.Slug, r.Topic.Name, r.FirstVerse, r.LastVerse, r.Heading })
            .ToListAsync(cancellationToken);
        rows = [.. rows.Where(r => !IndexLists.Contains(r.Slug))];
        if (rows.Count == 0)
        {
            return [];
        }

        // A whole chapter, or a run to its end, is closed by the chapter's own length in the frame,
        // which is also what a topic's verses are a share of.
        var length = await db.VerseReferences
            .Where(r => r.CanonicalBook == book && r.CanonicalChapter == chapter)
            .MaxAsync(r => (int?)r.CanonicalVerse, cancellationToken) ?? 0;

        var topics = rows.GroupBy(r => (r.Slug, r.Name))
            .Select(topic =>
            {
                var runs = topic
                    .Select(r =>
                    {
                        var first = r.FirstVerse ?? 1;
                        return (Heading: Heading(r.Heading), First: first,
                            Last: r.LastVerse ?? Math.Max(first, length), Whole: r.FirstVerse is null);
                    })
                    .ToList();
                var entries = runs
                    .GroupBy(run => run.Heading)
                    .Select(line => new ContextTopicEntryResponse(
                        line.Key,
                        [.. line.SelectMany(run => Enumerable.Range(run.First, run.Last - run.First + 1)).Distinct().Order()],
                        line.Any(run => run.Whole)))
                    .OrderBy(e => e.Verses.Count == 0 ? 0 : e.Verses[0])
                    .ThenBy(e => e.Heading, StringComparer.Ordinal)
                    .ToList();
                var verses = entries.SelectMany(e => e.Verses).Distinct().Order().ToList();
                var named = runs.Where(run => !run.Whole)
                    .SelectMany(run => Enumerable.Range(run.First, run.Last - run.First + 1))
                    .Distinct()
                    .Count();
                return (Topic: new ContextTopicResponse(topic.Key.Slug, Title(topic.Key.Name), verses, entries),
                    topic.Key.Name, Named: named, Whole: runs.Any(run => run.Whole));
            })
            .ToList();

        var said = await weights.Said(book, chapter, () =>
            Said(db, book, chapter, [.. topics.Select(t => (t.Topic.Slug, t.Name, t.Topic.Verses))], cancellationToken));

        return
        [
            .. topics
                .Select(t =>
                {
                    var says = said.Contains(t.Topic.Slug);
                    var (group, record) = Group(t.Topic.Slug, t.Name, t.Named == 0 && t.Whole, weights, entities);
                    return t.Topic with
                    {
                        Group = group,
                        Said = says,
                        Record = record,
                        Relevance = Relevance(
                            t.Named, t.Whole, says, length, weights.TopicVerses.GetValueOrDefault(t.Topic.Slug)),
                    };
                })
                .OrderBy(t => GroupOrder(t.Group))
                .ThenByDescending(t => t.Relevance)
                .ThenByDescending(t => t.Verses.Count)
                .ThenBy(t => t.Name, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// How much a topic is this chapter's: the share of its verses the topic files, a verse cited
    /// only as part of the whole chapter counting for a fraction of one, times how rare the topic is
    /// in the Bible, and more where the chapter's words say it.
    /// </summary>
    internal static double Relevance(int named, bool whole, bool said, int length, int versesInBible)
    {
        if (length <= 0)
        {
            return 0;
        }

        var wholeWeight = said ? WholeChapterSaid : WholeChapterUnsaid;
        var covered = named + (whole ? wholeWeight * Math.Max(0, length - named) : 0);
        var rarity = Math.Log((double)ContextWeights.BibleVerses / Math.Max(1, versesInBible));
        return covered / length * Math.Max(rarity, 0) * (said ? SaidBonus : 1);
    }

    /// <summary>
    /// Whether a topic is a theme of the chapter, the entry of a person its panel lists, or a reading
    /// of the chapter as about someone it never names; and whose row or record that is.
    ///
    /// <para>
    /// A topic belongs on a row when it is about the row's record, or bears the name the row does:
    /// Nave's EVE files more of Genesis 2 and 3 than the record of Eve lists, and is still Eve's.
    /// </para>
    /// </summary>
    private static (string Group, string? Record) Group(
        string slug,
        string name,
        bool wholeOnly,
        ContextWeights weights,
        IReadOnlyList<ContextEntityResponse> entities)
    {
        var head = ContextWeights.Head(name);
        var subject = weights.About.GetValueOrDefault(slug);
        var row = entities.FirstOrDefault(e => subject?.Slugs.Contains(e.Slug) == true)
                  ?? entities.FirstOrDefault(e => e.Group is null
                                                  && (ContextWeights.Head(e.Name) == head
                                                      || (e.ChapterName is { } used && ContextWeights.Head(used) == head)));
        if (row is not null)
        {
            return row.Kind == EnumSpelling.Of(EntityKind.Person) ? (Groups.Person, row.Slug) : (Groups.Theme, null);
        }

        var about = subject?.Slugs.Count == 1 ? subject.Slugs.First() : null;
        if (subject is { Personal: true })
        {
            return (Groups.Reading, about);
        }

        // A place the index files a whole chapter under and the chapter never names is the index's
        // outline of a book: Nave files his outline of Romans under Rome.
        return wholeOnly && weights.Places.Contains(head) ? (Groups.Reading, about) : (Groups.Theme, null);
    }

    private static int GroupOrder(string group) => group switch
    {
        Groups.Theme => 0,
        Groups.Person => 1,
        _ => 2,
    };

    /// <summary>
    /// The topics whose heading the chapter's English says: every word of the heading that carries
    /// meaning stands in one verse the topic files, in some English text of the corpus. <em>Good and
    /// Evil</em> is said in Genesis 3:5; <em>Fall of Man</em> is said nowhere in Genesis 3, and is
    /// Nave's name for what happens in it.
    /// </summary>
    private static async Task<IReadOnlySet<string>> Said(
        AppDbContext db,
        int book,
        int chapter,
        IReadOnlyList<(string Slug, string Name, IList<int> Verses)> topics,
        CancellationToken cancellationToken)
    {
        var words = await (
                from reference in db.VerseReferences
                where reference.IsPrimary
                      && reference.CanonicalBook == book
                      && reference.CanonicalChapter == chapter
                join word in db.Words on reference.VerseId equals word.VerseId
                join text in db.Texts on word.TextId equals text.Id
                where text.Language == English
                select new { reference.CanonicalVerse, Form = word.NormalisedText ?? word.Surface })
            .Distinct()
            .ToListAsync(cancellationToken);

        var stems = new Dictionary<int, HashSet<string>>();
        foreach (var word in words)
        {
            if (!stems.TryGetValue(word.CanonicalVerse, out var at))
            {
                stems[word.CanonicalVerse] = at = new HashSet<string>(StringComparer.Ordinal);
            }

            at.UnionWith(HeadingWords.Stems(word.Form));
        }

        return topics
            .Where(topic =>
            {
                var heading = HeadingWords.Of(topic.Name);
                return heading.Count > 0 && topic.Verses.Any(verse =>
                    stems.TryGetValue(verse, out var at) && heading.All(at.Contains));
            })
            .Select(topic => topic.Slug)
            .ToHashSet(StringComparer.Ordinal);
    }

    private const string Joiner = " — ";

    /// <summary>A line of an entry as it reads; the parts Nave set in capitals, as a heading is written.</summary>
    private static string? Heading(string? heading) =>
        heading is null
            ? null
            : string.Join(Joiner, heading.Split(Joiner).Select(part =>
                part.Any(char.IsLower) || !part.Any(char.IsLetter) ? part : Title(part)));

    private static readonly HashSet<string> SmallWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "as", "at", "by", "for", "from", "in", "into", "of", "on", "or", "the", "to", "with",
    };

    /// <summary>
    /// A heading as a reader writes one: Nave prints <c>LORD'S SUPPER</c> and <c>GOD, SOVEREIGNTY OF</c>,
    /// and a list of them in capitals reads as shouting. Each word is capitalised, and each part of a
    /// hyphenated name, except the small words inside a heading.
    /// </summary>
    internal static string Title(string name)
    {
        var lower = name.ToLower(CultureInfo.InvariantCulture);
        var title = new StringBuilder(lower.Length);
        var words = lower.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0)
            {
                title.Append(' ');
            }

            var word = words[i];
            if (i > 0 && SmallWords.Contains(word.TrimEnd(',', ';', ':', '.')))
            {
                title.Append(word);
                continue;
            }

            var start = true;
            foreach (var c in word)
            {
                title.Append(start && char.IsLetter(c) ? char.ToUpper(c, CultureInfo.InvariantCulture) : c);
                if (char.IsLetter(c))
                {
                    start = false;
                }
                else if (c == '-')
                {
                    start = true;
                }
            }
        }

        return title.ToString();
    }
}

/// <param name="Name">The heading, capitalised as a heading is; English, as Nave wrote it.</param>
/// <param name="Verses">Every verse of this chapter the topic files.</param>
/// <param name="Entries">The lines of the entry that cite this chapter, each with its verses.</param>
internal record ContextTopicResponse(string Slug, string Name, IList<int> Verses, IList<ContextTopicEntryResponse> Entries)
{
    /// <summary>
    /// <c>theme</c> for a subject of the chapter; <c>person</c> for the entry of a person the chapter
    /// names, which belongs on that person's row; <c>reading</c> for a topic about a person the
    /// chapter never names, which is how the index reads the chapter.
    /// </summary>
    public string Group { get; init; } = ChapterTopics.Groups.Theme;

    /// <summary>Whether the chapter's own English says the heading, in a verse the topic files.</summary>
    public bool Said { get; init; }

    /// <summary>The record a person topic belongs to, or a reading is about where one record bears the name.</summary>
    public string? Record { get; init; }

    /// <summary>What the topics are ordered by within their group; not a figure for a reader.</summary>
    [JsonIgnore]
    public double Relevance { get; init; }
}

/// <param name="Heading">The line the verses are filed under — <em>Makes the golden calf</em>; null where the topic cites them alone.</param>
/// <param name="Verses">The verses of this chapter the line cites.</param>
/// <param name="WholeChapter">Whether the line cites the whole chapter rather than verses of it.</param>
internal record ContextTopicEntryResponse(string? Heading, IList<int> Verses, bool WholeChapter);
