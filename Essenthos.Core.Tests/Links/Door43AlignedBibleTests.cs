using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Door43;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The older translationCore exports, which name many original words by spelling alone: a milestone
/// with an empty Strong number is a span where the source is one of those, and is what it always was
/// everywhere else.
/// </summary>
public sealed class Door43UnnumberedSpanTests
{
    private const string Verse =
        """
        \c 1
        \v 1 \zaln-s | x-strong="" x-lemma="" x-morph="" x-occurrence="1" x-occurrences="1" x-content="Βίβλος"\*\w kitab|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
        \zaln-s | x-strong="G10780" x-lemma="γένεσις" x-morph="Gr,N,,,,,GFS," x-occurrence="1" x-occurrences="1" x-content="γενέσεως"\*\w nasab|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
        """;

    [Fact]
    public void AMilestoneNamingItsWordBySpellingAloneIsASpanWhereTheSourceIsUnnumbered() =>
        Usfm3AlignmentReader.Read(Verse, unnumbered: true).Single().Spans.Select(span => span.Content)
            .Should().Equal("Βίβλος", "γενέσεως");

    [Fact]
    public void ElsewhereOnlyANumberedMilestoneIsASpan() =>
        Usfm3AlignmentReader.Read(Verse).Single().Spans.Select(span => span.Content).Should().Equal("γενέσεως");

    [Fact]
    public void EveryAlignedBibleIsKnownToTheInterlinearLoader()
    {
        foreach (var bible in Door43AlignedBible.All)
        {
            InterlinearLinkLoader.Interlinear(bible.Slug).Should().Be((bible.Folder, bible.Source));
        }
    }
}
