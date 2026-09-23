using System.Globalization;
using System.Text;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The subjects of Nave's Topical Bible that file verses of one chapter, the ones filing most of its
/// verses first. On trial: nothing else reads the topics, and whether they stay is the owner's call.
/// </summary>
internal static class ChapterTopics
{
    internal static async Task<IList<ContextTopicResponse>> InChapter(
        AppDbContext db,
        int book,
        int chapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.TopicReferences
            .Where(r => r.CanonicalBook == book && r.CanonicalChapter == chapter)
            .Select(r => new { r.Topic!.Slug, r.Topic.Name, r.FirstVerse, r.LastVerse, r.Heading })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        // A whole chapter, or a run to its end, is closed by the chapter's own length in the frame.
        var length = rows.Any(r => r.LastVerse is null)
            ? await db.VerseReferences
                .Where(r => r.CanonicalBook == book && r.CanonicalChapter == chapter)
                .MaxAsync(r => (int?)r.CanonicalVerse, cancellationToken) ?? 0
            : 0;

        return
        [
            .. rows.GroupBy(r => (r.Slug, r.Name))
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
                    return new ContextTopicResponse(topic.Key.Slug, Title(topic.Key.Name), verses, entries);
                })
                .OrderByDescending(t => t.Verses.Count)
                .ThenBy(t => t.Name, StringComparer.Ordinal),
        ];
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
/// <param name="Verses">Every verse of this chapter the topic files, which is what the topics are ordered by.</param>
/// <param name="Entries">The lines of the entry that cite this chapter, each with its verses.</param>
internal record ContextTopicResponse(string Slug, string Name, IList<int> Verses, IList<ContextTopicEntryResponse> Entries);

/// <param name="Heading">The line the verses are filed under — <em>Makes the golden calf</em>; null where the topic cites them alone.</param>
/// <param name="Verses">The verses of this chapter the line cites.</param>
/// <param name="WholeChapter">Whether the line cites the whole chapter rather than verses of it.</param>
internal record ContextTopicEntryResponse(string? Heading, IList<int> Verses, bool WholeChapter);
