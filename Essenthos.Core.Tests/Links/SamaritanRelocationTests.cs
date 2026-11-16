using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A run of verses the Masoretic numbers and the Samaritan does not is placed in a Samaritan verse's surplus only when
/// that surplus holds most of the run's words of two letters or more: the conjunction, the article and the prepositions
/// pair with their like anywhere, so they cannot carry a passage to another place.
/// </summary>
public sealed class SamaritanRelocationTests
{
    private static readonly string[] Run = ["ו", "את", "כל", "ה", "ארץ", "גלעד", "עד", "דן", "ו", "את", "כל", "נפתלי"];

    [Fact]
    public void A_distant_surplus_that_shares_only_short_words_takes_no_passage()
    {
        var (placed, moved, drafts) = Relocate(["ו", "את", "כל", "ה", "ארץ", "ו", "ה", "ו", "ה", "הר", "גרזים"]);

        (placed, moved).Should().Be((0, 0));
        drafts.Should().NotContain(d => d.Relation == LinkRelation.Transposes);
        drafts.Should().OnlyContain(d => d.Relation == LinkRelation.Expands, "the surplus stays what the Samaritan has and the Masoretic has not");
    }

    [Fact]
    public void A_distant_surplus_that_holds_the_passage_transposes_it()
    {
        var (placed, moved, drafts) = Relocate(Run);

        (placed, moved).Should().Be((2, Run.Length));
        drafts.Count(d => d.Relation == LinkRelation.Transposes).Should().Be(Run.Length);
    }

    private static (int Placed, int Moved, List<SamaritanLinks.HebrewDraft> Drafts) Relocate(string[] surplus)
    {
        long id = 0;
        List<SamaritanLinks.HebrewWord> Words(IEnumerable<string> letters) =>
            [.. letters.Select(l => new SamaritanLinks.HebrewWord(id++, new WitnessForm(l, l)))];

        var host = (5, 5, 21);
        var hostWords = Words(surplus);
        var here = new Dictionary<(int Book, int Chapter, int Verse), List<SamaritanLinks.HebrewWord>> { [host] = hostWords };
        var there = new Dictionary<(int Book, int Chapter, int Verse), List<SamaritanLinks.HebrewWord>>
        {
            [host] = [],
            [(5, 34, 2)] = Words(Run[..6]),
            [(5, 34, 3)] = Words(Run[6..]),
        };
        var drafts = hostWords.Select(w => new SamaritanLinks.HebrewDraft(LinkRelation.Expands, [w.Id], [], 1)).ToList();
        var expandedAt = new Dictionary<(int Book, int Chapter, int Verse), List<SamaritanLinks.HebrewDraft>> { [host] = [.. drafts] };

        var (placed, moved, _) = SamaritanLinks.Relocate(here, there, expandedAt, drafts);
        return (placed, moved, drafts);
    }
}
