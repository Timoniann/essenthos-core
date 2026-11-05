using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// The review of the model's readings: what it overturned, how each overturned reading is disposed of, and what a
/// later run replaced. A file quietly emptied by a bad regeneration fails here.
/// </summary>
public sealed class SenseReadingTests
{
    /// <summary>
    /// The whole review, asserted as counts so that a file quietly emptied by a bad regeneration
    /// fails the build instead of silently loading answers somebody has already shown to be wrong —
    /// or silently dropping the records the same review asked for.
    /// </summary>
    [Fact]
    public void TheReviewFilesOverturnSeventyFourReadingsAndDisposeOfEveryOne()
    {
        var refused = SenseReadingFiles.Refused().Readings;

        refused.Should().HaveCount(74);
        refused.Count(r => r.Verdict == "wrong").Should().Be(61);
        refused.Count(r => r.Verdict == "model-wrong").Should().Be(7);
        refused.Count(r => r.Verdict == "both-wrong").Should().Be(4);
        refused.Count(r => r.Verdict == "contested").Should().Be(2);
        refused.Select(r => r.Word).Should().OnlyHaveUniqueItems();
        refused.Should().OnlyContain(r => r.Why.Length > 0);

        var review = SenseReadingFiles.ReviewRulings().Rulings;
        var owner = SenseReadingFiles.Rulings().Rulings;
        var replaced = SenseReadingFiles.Superseded().Readings.ToDictionary(r => r.Word);

        review.Should().HaveCount(33);
        review.Count(r => r.Existing is not null).Should().Be(21);
        review.Count(r => r.Create is not null).Should().Be(12);
        review.Select(r => r.Word).Should().NotIntersectWith(owner.Select(r => r.Word));

        // Every overturned reading is disposed of, and none of the four ways is silence: a later run
        // replaced the answer the verdict was about, a ruling of the review names the referent, the
        // owner ruled on it, or the referent is the tribe of Benjamin, which the peoples' rulings give
        // it. Which ruling answers which reading is asked of the words both name; here the counts are held.
        // The owner ruled on five of the overturned words, and the re-ask replaced one of those too.
        const int ruledByTheOwner = 4;
        var standing = refused.Where(r => !replaced.ContainsKey(r.Word)).ToList();
        var tribe = PeopleFiles.Read().Rulings;

        standing.Should().HaveCount(review.Count + ruledByTheOwner + tribe.Count);
        standing.Count(r => r.StrongNumber == "H1144").Should().Be(tribe.Count);
        tribe.Should().HaveCount(8).And.OnlyContain(r => r.People == "benjaminites");
    }

    /// <summary>
    /// The record of what a later run replaced, asserted the same way and for the same reason: this
    /// file is the only thing standing between the corpus and the answers the re-ask overturned.
    /// </summary>
    [Fact]
    public void TheLaterRunReplacesFourHundredAndEightyAnswers()
    {
        var replaced = SenseReadingFiles.Superseded();

        replaced.Readings.Should().HaveCount(480);
        replaced.Readings.Select(r => r.Word).Should().OnlyHaveUniqueItems();
        replaced.Readings.Should().OnlyContain(r => r.Was != r.Now);
        replaced.Model.Should().NotBeEmpty();
        replaced.PromptVersion.Should().NotBeEmpty();
    }
}
