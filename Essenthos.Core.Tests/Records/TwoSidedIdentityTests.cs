using System.Text.RegularExpressions;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// If one person may be another, both pages say so (RUL-0201): a reader who reaches either is told that
/// the text does not settle whether they are namesakes or one man. It is said in two ways, and both are
/// held here as the shipped files write them: an alternative («Ким це ще може бути») on the record,
/// whose <see cref="OwnAlternative.Back"/> says it on the other; and a note that says <em>He may also
/// be …</em> or that a tradition <em>identifies him with …</em>, citing the other man's verses, which
/// the other man's note has to answer. The owner's rulings that two are different people are the one
/// exception, and there neither page offers the other.
/// </summary>
public sealed partial class TwoSidedIdentityTests
{
    [Fact]
    public void EveryAlternativeIsSaidOnBothRecords() =>
        OneSided(Alternatives(), Folded(), SenseReadingFiles.HeldApart().Pairs).Should().BeEmpty();

    [Fact]
    public void NoAlternativeJoinsTwoTheOwnerHeldApart()
    {
        var apart = SenseReadingFiles.HeldApart().Pairs;
        apart.Should().Contain(pair => pair.Joins("hobab", "reuel-2"));
        Alternatives().Where(said => apart.Any(pair => pair.Joins(said.Record, said.Other))).Should().BeEmpty();
    }

    [Fact]
    public void AnAlternativeSaidOnOneRecordOnlyIsFound()
    {
        Said[] said =
        [
            new("azariah", "uzziah", HasBack: false),
            new("joel", "shaul", HasBack: true),
            new("tormah", "arumah", HasBack: false),
            new("arumah", "tormah", HasBack: false),
            new("hobab", "reuel", HasBack: false),
            new("judas-of-james", "thaddaeus", HasBack: false),
            new("adah", "basemath", HasBack: false),
            new("mary-7", "mary", HasBack: true),
            new("mary", "mary-7", HasBack: false),
        ];
        var folded = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["judas-of-james"] = "thaddaeus",
            ["basemath"] = "adah",
        };
        HeldApartPair[] apart = [new("reuel", "hobab", "two men", "a test")];

        OneSided(said, folded, apart).Should().BeEquivalentTo(
            ["azariah may be uzziah, and uzziah does not say so", "mary-7 may be mary, and mary says so twice"]);
    }

    [Fact]
    public void EveryNoteThatOffersAnotherManIsAnsweredOnHisPage() =>
        Unanswered(Notes(), Verses()).Should().BeEmpty();

    [Fact]
    public void ANoteAnsweredOnNeitherSideIsFound()
    {
        var notes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["nathanael"] = ["Philip found him (JHN 1:45). A tradition identifies him with Bartholomew (MAT 10:3; MRK 3:18); no verse says so."],
            ["bartholomew"] = [],
            ["sosthenes"] = ["He may also be the brother of 1 Corinthians (1CO 1:1); the text does not say."],
            ["sosthenes-the-brother"] = ["He may also be the ruler of the synagogue (ACT 18:17); the text does not say."],
        };
        var verses = new Dictionary<string, IReadOnlyList<ScriptureSpan>>(StringComparer.Ordinal)
        {
            ["nathanael"] = [ScriptureSpan.Parse("JHN 1:43-51")],
            ["bartholomew"] = [ScriptureSpan.Parse("MAT 10:3")],
            ["sosthenes"] = [ScriptureSpan.Parse("ACT 18:17")],
            ["sosthenes-the-brother"] = [ScriptureSpan.Parse("1CO 1:1")],
        };

        Unanswered(notes, verses).Should().Equal("nathanael: MAT 10:3; MRK 3:18");

        notes["bartholomew"] = ["He may also be Nathanael (JHN 1:45-49; 21:2); the text does not say."];
        Unanswered(notes, verses).Should().BeEmpty();
    }

    [Theory]
    [InlineData("(JHN 1:45-49; 21:2)", "JHN 1:45-49|JHN 21:2")]
    [InlineData("(COL 1:7; 4:12; PHM 1:23)", "COL 1:7|COL 4:12|PHM 1:23")]
    [InlineData("(GEN 36:2, 36:24) and (1CH 4:18)", "GEN 36:2|GEN 36:24|1CH 4:18")]
    public void ANoteCitesTheVersesItsParenthesesName(string sentence, string expected) =>
        Cited(sentence).Should().Equal(expected.Split('|'));

    /// <summary>One alternative a file gives a record: who it is on, whom it names, and whether it says so back.</summary>
    private sealed record Said(string Record, string Other, bool HasBack);

    /// <summary>
    /// The pairs said on one record only, and the ones said back twice (a <c>back</c> beside the other
    /// record's own alternative naming this one, which would show the row twice). A pair whose two
    /// records are folded into one is no longer two people; a pair the owner held apart is the other
    /// test's to refuse.
    /// </summary>
    private static List<string> OneSided(
        IReadOnlyList<Said> said,
        IReadOnlyDictionary<string, string> folded,
        IReadOnlyList<HeldApartPair> apart)
    {
        string Kept(string slug)
        {
            while (folded.TryGetValue(slug, out var kept))
            {
                slug = kept;
            }

            return slug;
        }

        var found = new List<string>();
        foreach (var one in said)
        {
            if (Kept(one.Record) == Kept(one.Other) || apart.Any(pair => pair.Joins(one.Record, one.Other)))
            {
                continue;
            }

            var back = said.Count(other => other.Record == one.Other && other.Other == one.Record) + (one.HasBack ? 1 : 0);
            if (back == 0)
            {
                found.Add($"{one.Record} may be {one.Other}, and {one.Other} does not say so");
            }
            else if (back > 1 && one.HasBack)
            {
                found.Add($"{one.Record} may be {one.Other}, and {one.Other} says so twice");
            }
            else if (Kept(one.Record) != one.Record || Kept(one.Other) != one.Other)
            {
                found.Add($"{one.Record} may be {one.Other}, and one of them is folded into another record");
            }
        }

        return found;
    }

    /// <summary>Every alternative naming a record, as the rulings and the titles write it.</summary>
    private static List<Said> Alternatives() =>
    [
        .. SenseReadingFiles.AllRulings()
            .SelectMany(file => file.Rulings)
            .SelectMany(ruling => (ruling.Alternatives ?? [])
                .Where(alternative => alternative.Slug is not null)
                .Select(alternative => new Said(ruling.Create?.Slug ?? ruling.Existing!, alternative.Slug!, alternative.Back is not null))),
        .. SenseReadingFiles.Titles().Titles
            .SelectMany(title => (title.Alternatives ?? [])
                .Where(alternative => alternative.Slug is not null)
                .Select(alternative => new Said(title.Slug, alternative.Slug!, alternative.Back is not null))),
    ];

    private static Dictionary<string, string> Folded() =>
        DuplicateRecordLoader.Read().Merges
            .GroupBy(pair => pair.Folds, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Keeps, StringComparer.Ordinal);

    /// <summary>
    /// The notes whose sentence offers another man, each answered where some record standing at a verse
    /// the sentence cites offers this record back, citing one of its verses. A sentence citing no
    /// record's verse is unanswered too: a page it cannot be checked against is no answer.
    /// </summary>
    private static List<string> Unanswered(
        IReadOnlyDictionary<string, IReadOnlyList<string>> notes,
        IReadOnlyDictionary<string, IReadOnlyList<ScriptureSpan>> verses)
    {
        var offers = notes.ToDictionary(
            note => note.Key,
            note => note.Value.SelectMany(Offers).SelectMany(Cited).Select(ScriptureSpan.TryParse).OfType<ScriptureSpan>().ToList(),
            StringComparer.Ordinal);

        bool Meets(IEnumerable<ScriptureSpan> cited, string slug) =>
            verses.TryGetValue(slug, out var held) && cited.Any(span => held.Any(verse => Overlap(span, verse)));

        var found = new List<string>();
        foreach (var (slug, cited) in offers.Where(offer => offer.Value.Count > 0))
        {
            var answered = verses.Keys
                .Where(other => other != slug && Meets(cited, other))
                .Any(other => offers.TryGetValue(other, out var back) && Meets(back, slug));
            if (!answered)
            {
                found.Add($"{slug}: {string.Join("; ", notes[slug].SelectMany(Offers).SelectMany(Cited))}");
            }
        }

        return found;
    }

    private static bool Overlap(ScriptureSpan a, ScriptureSpan b) =>
        a.Holds(b.Book, b.FromChapter, b.FromVerse ?? 1, 1) || b.Holds(a.Book, a.FromChapter, a.FromVerse ?? 1, 1);

    /// <summary>The sentences of a note that offer another man.</summary>
    private static IEnumerable<string> Offers(string note) =>
        Sentence().Split(note).Where(sentence => Offering().IsMatch(sentence));

    /// <summary>Every verse a sentence cites in parentheses, a book carried on to the verses after it.</summary>
    private static List<string> Cited(string sentence)
    {
        var cited = new List<string>();
        foreach (Match group in Parenthesis().Matches(sentence))
        {
            string? book = null;
            foreach (var part in group.Groups[1].Value.Split([';', ','], StringSplitOptions.TrimEntries))
            {
                if (Citation().Match(part) is not { Success: true } citation)
                {
                    continue;
                }

                book = citation.Groups["book"].Success ? citation.Groups["book"].Value : book;
                if (book is not null)
                {
                    cited.Add($"{book} {citation.Groups["verse"].Value}");
                }
            }
        }

        return cited;
    }

    /// <summary>Every record's verses, as the rulings place it and the narrative files give its passages.</summary>
    private static Dictionary<string, IReadOnlyList<ScriptureSpan>> Verses()
    {
        var verses = new Dictionary<string, List<ScriptureSpan>>(StringComparer.Ordinal);
        void Add(string slug, string written)
        {
            if (ScriptureSpan.TryParse(written) is { } span)
            {
                (verses.TryGetValue(slug, out var list) ? list : verses[slug] = []).Add(span);
            }
        }

        foreach (var ruling in SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings))
        {
            Add(ruling.Create?.Slug ?? ruling.Existing!, ruling.Reference);
        }

        foreach (var thing in Things())
        foreach (var passage in thing.Passages ?? [])
        {
            Add(thing.Slug, passage.Reference);
        }

        return verses.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ScriptureSpan>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>Every note the rulings and the narrative files write on a record.</summary>
    private static Dictionary<string, IReadOnlyList<string>> Notes()
    {
        var notes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string slug, string? note)
        {
            if (!string.IsNullOrWhiteSpace(note))
            {
                (notes.TryGetValue(slug, out var list) ? list : notes[slug] = []).Add(note);
            }
        }

        foreach (var ruling in SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings))
        {
            Add(ruling.Create?.Slug ?? ruling.Existing!, ruling.Create?.Notes);
            Add(ruling.Create?.Slug ?? ruling.Existing!, ruling.Says?.Notes);
        }

        foreach (var thing in Things())
        {
            Add(thing.Slug, thing.Notes);
        }

        return notes.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);
    }

    private static IEnumerable<ThingRecord> Things() =>
        ThingFiles.Read(ThingSet.MadeAndKept).Records.Concat(ThingFiles.Read(ThingSet.Narratives).Records);

    [GeneratedRegex(@"(?<=\.)\s+(?=[A-Z])")]
    private static partial Regex Sentence();

    [GeneratedRegex(@"\b(?:He|She) may also be\b|\bidentifies (?:him|her) with\b")]
    private static partial Regex Offering();

    [GeneratedRegex(@"\(([^()]*)\)")]
    private static partial Regex Parenthesis();

    [GeneratedRegex(@"^(?:(?<book>[1-3]?[A-Z]{2,3})\s+)?(?<verse>\d+:\d+(?:-\d+)?)$")]
    private static partial Regex Citation();
}
