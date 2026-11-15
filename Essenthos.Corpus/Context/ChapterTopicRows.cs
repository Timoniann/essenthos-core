using System.Globalization;
using System.Text;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Context;

/// <summary>
/// What the chapter context reads of Nave's Topical Bible for one chapter before weighing it: which topics are the index's
/// own lists, how a heading reads, and which headings the chapter's own English says.
/// </summary>
internal static class ChapterTopicRows
{
    /// <summary>Nave's lists of passages, filed as topics but about nothing a chapter says.</summary>
    internal static readonly IReadOnlySet<string> IndexLists =
        new HashSet<string>(StringComparer.Ordinal) { "quotationsandallusions", "readingsselect", "psalms" };

    /// <summary>The language a heading is looked for in, since Nave wrote in it.</summary>
    private const string English = "eng";

    /// <summary>
    /// The topics whose heading the chapter's English says: every word of the heading that carries
    /// meaning stands in one verse the topic files, in some English text of the corpus. <em>Good and
    /// Evil</em> is said in Genesis 3:5; <em>Fall of Man</em> is said nowhere in Genesis 3, and is
    /// Nave's name for what happens in it.
    /// </summary>
    internal static async Task<IReadOnlySet<string>> Said(
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
    internal static string? Heading(string? heading) =>
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
