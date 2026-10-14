using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The kind of way of knowing each of this project's references carries, which a page names in the
/// reader's language where it would otherwise say "Essenthos" once per way.
/// </summary>
public sealed class ReferenceMethodTests
{
    /// <summary>
    /// Jesus's verses come three ways: the words the corpus marks, and the verse read by two models,
    /// with a third where they differed. The first is one kind and the two readings are another.
    /// </summary>
    [Fact]
    public void TheWaysJesussVersesAreKnownAreTwoKinds()
    {
        ReferenceMethods.Of("Essenthos, from the words this corpus annotates to the person or the place they name")
            .Should().Be(ReferenceMethods.AnnotatedWords);
        ReferenceMethods.Of(
                "Essenthos, read from the verse by two language models, claude-sonnet-5-5 and claude-opus-5-5, "
                + "on 2026-09-30: the verse speaks of the record and does not name it")
            .Should().Be(ReferenceMethods.ModelReading);
        ReferenceMethods.Of(
                "Essenthos, read from the verse by two language models, claude-sonnet-5-5 and claude-opus-5-5, "
                + "on 2026-09-30, and by claude-opus-5-5 a third time where the two differed: the verse speaks "
                + "of the record and does not name it")
            .Should().Be(ReferenceMethods.ModelReading);
    }

    /// <summary>Every sentence the shipped verse readings write is a reading by models.</summary>
    [Fact]
    public void EverySentenceTheVerseReadingsWriteIsAModelReading()
    {
        var file = VerseReadingFiles.Read();

        file.ReferenceSources.Should().OnlyContain(source => ReferenceMethods.Of(source) == ReferenceMethods.ModelReading);
    }

    [Theory]
    [InlineData("Essenthos, from the words this corpus annotates to the people", ReferenceMethods.AnnotatedWords)]
    [InlineData("Essenthos, from Strong's Dictionary entry for the word and every word BHSA numbers with it", ReferenceMethods.Lexicon)]
    [InlineData("Essenthos, from the passages that command it, as a language model read them on 2026-09-23", ReferenceMethods.ModelReading)]
    [InlineData("Essenthos, on the project owner's ruling of 2026-09-06", ReferenceMethods.OwnerRuling)]
    [InlineData("Essenthos, on the project owner's decision of 2026-09-16 that a namesake the text does not identify is a person of his own", ReferenceMethods.OwnerRuling)]
    [InlineData("Essenthos, on the report of records holding several people, each occurrence read against its verse by claude-opus-5-5, 2026-09-25", ReferenceMethods.Review)]
    [InlineData("Essenthos, the verse a relationship of this project's own was read from, which concerns the record", ReferenceMethods.Relationship)]
    public void EachDeclaredBeginningHasItsKind(string source, string method) =>
        ReferenceMethods.Of(source).Should().Be(method);

    /// <summary>Another dataset's references, and a sentence of ours nobody declared, have no kind.</summary>
    [Theory]
    [InlineData("BibleData, bibledata.org")]
    [InlineData("Essenthos, a way of knowing nobody has declared yet")]
    [InlineData(null)]
    public void WhatIsNotDeclaredHasNoKind(string? source) =>
        ReferenceMethods.Of(source).Should().BeNull();
}
