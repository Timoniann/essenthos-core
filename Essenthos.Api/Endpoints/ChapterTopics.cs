using System.Text.Json.Serialization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;

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
/// person's entry, and is not sent: under the person's row it added nothing a reader could use
/// (owner, 2026-11-14). One about a person the chapter names nowhere —
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

    /// <summary>What a verse cited only as part of the whole chapter counts for where the words say the topic.</summary>
    internal const double WholeChapterSaid = 0.5;

    /// <summary>What a verse cited only as part of the whole chapter counts for where they do not.</summary>
    internal const double WholeChapterUnsaid = 0.25;

    /// <summary>How much more a topic counts when the chapter's own English says its heading.</summary>
    internal const double SaidBonus = 1.5;

    /// <summary>A heading as a reader writes one (<see cref="ChapterTopicRows.Title"/>).</summary>
    internal static string Title(string name) => ChapterTopicRows.Title(name);

    /// <summary>The chapter's topics weighed against the records the panel shows, the most particular to it first.</summary>
    internal static IList<ContextTopicResponse> Ranked(
        ChapterSkeleton skeleton,
        ContextWeights weights,
        IReadOnlyList<ContextEntityResponse> entities)
    {
        var said = skeleton.Said.ToHashSet(StringComparer.Ordinal);
        return
        [
            .. skeleton.Topics
                .Select(t =>
                {
                    var says = said.Contains(t.Slug);
                    var (group, record) = Group(t.Slug, t.Name, t.Named == 0 && t.Whole, weights, entities);
                    return new ContextTopicResponse(t.Slug, t.Title, t.Verses, t.Entries)
                    {
                        Group = group,
                        Said = says,
                        Record = record,
                        Relevance = Relevance(t.Named, t.Whole, says, skeleton.Length, weights.TopicVerses.GetValueOrDefault(t.Slug)),
                    };
                })
                // A person's own entry is told apart so that it is not taken for a theme, and then left
                // out, since the panel no longer shows it under the person's row.
                .Where(t => t.Group != Groups.Person)
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
}

/// <param name="Name">The heading, capitalised as a heading is; English, as Nave wrote it.</param>
/// <param name="Verses">Every verse of this chapter the topic files.</param>
/// <param name="Entries">The lines of the entry that cite this chapter, each with its verses.</param>
internal record ContextTopicResponse(string Slug, string Name, IList<int> Verses, IList<ContextTopicEntryResponse> Entries)
{
    /// <summary>
    /// <c>theme</c> for a subject of the chapter, or <c>reading</c> for a topic about a person the
    /// chapter never names, which is how the index reads the chapter. A person's own entry, which
    /// <see cref="ChapterTopics.Groups.Person"/> names, is not sent.
    /// </summary>
    public string Group { get; init; } = ChapterTopics.Groups.Theme;

    /// <summary>Whether the chapter's own English says the heading, in a verse the topic files.</summary>
    public bool Said { get; init; }

    /// <summary>The record a reading is about, where one record bears the name.</summary>
    public string? Record { get; init; }

    /// <summary>What the topics are ordered by within their group; not a figure for a reader.</summary>
    [JsonIgnore]
    public double Relevance { get; init; }
}
