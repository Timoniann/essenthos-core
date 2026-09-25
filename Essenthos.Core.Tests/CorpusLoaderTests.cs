using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The loader against a real database, with a text small enough to read. The two witnesses are
/// loaded by running the application, not by a test: half a million words on every run buys nothing
/// that these do not already say.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CorpusLoaderTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public CorpusLoaderTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    /// <summary>
    /// A transaction is no good here: the loader opens its own, and COPY inside a rolled-back outer
    /// transaction is a different thing from COPY in the real one. So the fixture is cleaned either
    /// side instead, and everything under a text goes with it.
    /// </summary>
    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    private CorpusLoader Loader() => new(_db, NullLogger<CorpusLoader>.Instance);

    [Fact]
    public async Task ATextIsWrittenWithItsBooksChaptersVersesAndWords()
    {
        var outcome = await Loader().Load(Sample());

        outcome.AlreadyLoaded.Should().BeFalse();
        outcome.Books.Should().Be(1);
        outcome.Chapters.Should().Be(2);
        outcome.Verses.Should().Be(3);
        outcome.Words.Should().Be(6);

        var text = await _db.Texts.SingleAsync(t => t.Slug == "sample");
        text.Licence.Should().Be("CC0-1.0");
        text.Redistribution.Should().Be(Redistribution.PublicDomain);
    }

    /// <summary>
    /// Who made the text and which edition it is reach the row. A field the definition holds and
    /// the loader drops is worse than one nobody filled in: it reads as established and absent.
    /// </summary>
    [Fact]
    public async Task WhoMadeTheTextAndWhichEditionItIsAreWrittenWithIt()
    {
        await Loader().Load(Sample());

        var text = await _db.Texts.SingleAsync(t => t.Slug == "sample");
        text.Translators.Should().Be("Somebody");
        text.Editors.Should().Be("Somebody else");
        text.Edition.Should().Be("The second, revised");
        text.EditionYear.Should().Be(1769);
        text.About.Should().Be("What this text is.");
        text.RightsNote.Should().Be("What is unsettled about the rights.");
    }

    /// <summary>
    /// The whole reason there are two numbers. The loader must store the book's place in this text
    /// and its place in the shared order without conflating them.
    /// </summary>
    [Fact]
    public async Task ABooksPlaceInThisTextIsStoredApartFromItsCanonicalOrdinal()
    {
        await Loader().Load(Sample());

        var book = await _db.Books.SingleAsync();
        book.Position.Should().Be(1);
        book.CanonicalOrdinal.Should().Be(8);
    }

    /// <summary>
    /// A verse read back is the verse that was written, punctuation and spacing included. This is
    /// the property the load asserts on itself; here it is asserted about the load.
    /// </summary>
    [Fact]
    public async Task WhatTheDatabaseHoldsRebuildsTheVerse()
    {
        await Loader().Load(Sample());

        var rebuilt = await _db.Database
            .SqlQuery<string>(
                $"""
                 SELECT string_agg("text" || trailer, '' ORDER BY "position") AS "Value"
                 FROM word w JOIN verse v ON v.id = w.verse_id
                 WHERE v.chapter_number = 1 AND v.number = 1
                 """)
            .SingleAsync();

        rebuilt.Should().Be("In the beginning, God ");
    }

    [Fact]
    public async Task LoadingTwiceWritesNothingTheSecondTime()
    {
        await Loader().Load(Sample());
        var second = await Loader().Load(Sample());

        second.AlreadyLoaded.Should().BeTrue();
        (await _db.Words.CountAsync()).Should().Be(6);
        (await _db.Texts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SourceNotesCanBeAddedToAnAlreadyLoadedTextAndReachTheChapterResponse()
    {
        var source = Sample() with
        {
            Books = [
                new BookDraft(8, 1, "Ruth", "ruth", [
                    new ChapterDraft(1, [
                        new VerseDraft(1, [new WordDraft("In", " ")])
                        {
                            Notes = [
                                new VerseNoteDraft(VerseNoteKind.Footnote, "A source explanation.", 1),
                                new VerseNoteDraft(VerseNoteKind.CrossReference, "Genesis 1:1"),
                            ],
                        },
                    ]),
                ]),
            ],
        };
        await Loader().Load(source);

        var outcome = await new SourceNoteLoader(_db, NullLogger<SourceNoteLoader>.Instance).Load(source);
        var text = await _db.Texts.SingleAsync();
        var chapter = await Texts.ReadChapter(_db, text.Id, 8, 1, default);

        var anchor = await _db.Words.SingleAsync(word => word.Surface == "In");

        outcome.Notes.Should().Be(2);
        chapter.Should().ContainSingle().Which.Notes.Should().BeEquivalentTo([
            new SourceNoteResponse("footnote", "A source explanation.", anchor.Id, "In"),
            new SourceNoteResponse("cross-reference", "Genesis 1:1"),
        ]);
    }

    /// <summary>
    /// A text that gains a book after its notes were written gets that book's notes on the next
    /// pass, and the notes it had are not written again.
    /// </summary>
    [Fact]
    public async Task ABookATextGainsGetsItsNotes()
    {
        static BookDraft Noted(int ordinal, int position, string note) => new(
            ordinal, position, BookReferences.Name(ordinal), BookReferences.Slug(ordinal), [
                new ChapterDraft(1, [
                    new VerseDraft(1, [new WordDraft("In", "")])
                    {
                        Notes = [new VerseNoteDraft(VerseNoteKind.Footnote, note, 1)],
                    },
                ]),
            ]);

        var first = Sample() with { Books = [Noted(8, 1, "Ruth's note.")] };
        await Loader().Load(first);
        await new SourceNoteLoader(_db, NullLogger<SourceNoteLoader>.Instance).Load(first);

        var gained = first with { Books = [Noted(8, 1, "Ruth's note."), Noted(73, 2, "The Maccabees' note.")] };
        await Loader().AddMissingBooks(gained);
        var outcome = await new SourceNoteLoader(_db, NullLogger<SourceNoteLoader>.Instance).Load(gained);

        outcome.Notes.Should().Be(1);
        (await _db.VerseNotes.Select(note => note.Content).ToListAsync())
            .Should().BeEquivalentTo(["Ruth's note.", "The Maccabees' note."]);
        (await new SourceNoteLoader(_db, NullLogger<SourceNoteLoader>.Instance).Load(gained)).AlreadyLoaded
            .Should().BeTrue();
    }

    [Fact]
    public async Task ANoteOnARepeatedSpellingNamesTheWordItWasPrintedAfter()
    {
        var source = Sample() with
        {
            Books = [
                new BookDraft(8, 1, "Ruth", "ruth", [
                    new ChapterDraft(1, [
                        new VerseDraft(1, [
                            new WordDraft("Rachel", " "),
                            new WordDraft("and", " "),
                            new WordDraft("Rachel", ""),
                        ])
                        {
                            Notes = [new VerseNoteDraft(VerseNoteKind.Footnote, "The second one.", 3)],
                        },
                    ]),
                ]),
            ],
        };
        await Loader().Load(source);
        await new SourceNoteLoader(_db, NullLogger<SourceNoteLoader>.Instance).Load(source);

        var second = await _db.Words.SingleAsync(word => word.Surface == "Rachel" && word.Position == 3);
        var chapter = await Texts.ReadChapter(_db, (await _db.Texts.SingleAsync()).Id, 8, 1, default);

        chapter.Should().ContainSingle().Which.Notes.Should().ContainSingle()
            .Which.AnchorWordId.Should().Be(second.Id);
    }

    [Theory]
    [InlineData("Licence")]
    [InlineData("SourceUrl")]
    [InlineData("Redistribution")]
    public async Task ATextWithoutItsProvenanceIsRefused(string missing)
    {
        var definition = missing switch
        {
            "Licence" => Definition with { Licence = "   " },
            "SourceUrl" => Definition with { SourceUrl = "" },
            _ => Definition with { Redistribution = Redistribution.Unknown },
        };

        var attempt = () => Loader().Load(new TextSource(definition, Sample().Books));

        (await attempt.Should().ThrowAsync<InvalidOperationException>()).And.Message.Should().Contain(missing);
        (await _db.Texts.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// Whatever the corpus holds can be served and is copied into every release, so a text that
    /// may not be redistributed is refused before a row is written — the New World Translation's own
    /// definition included, whatever reader hands it over.
    /// </summary>
    [Fact]
    public async Task ATextThatMayNotBeRedistributedIsNeverWritten()
    {
        var prohibited = () => Loader().Load(
            new TextSource(Definition with { Redistribution = Redistribution.Prohibited }, Sample().Books));
        var newWorld = () => Loader().Load(new TextSource(NewWorldTextSource.Definition, Sample().Books));

        (await prohibited.Should().ThrowAsync<InvalidOperationException>()).And.Message.Should().Contain("never written");
        (await newWorld.Should().ThrowAsync<InvalidOperationException>()).And.Message.Should().Contain(NewWorldTextSource.Slug);
        (await _db.Texts.CountAsync()).Should().Be(0);
        (await _db.Words.CountAsync()).Should().Be(0);
    }

    private static readonly TextDefinition Definition = new(
        Slug: "sample",
        Name: "A sample",
        NameNative: null,
        Kind: TextKind.Translation,
        Language: "eng",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 1611,
        SourceUrl: "https://example.invalid/sample",
        RightsHolder: null,
        Licence: "CC0-1.0",
        LicenceUrl: "https://creativecommons.org/publicdomain/zero/1.0/",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: null)
    {
        Translators = "Somebody",
        Editors = "Somebody else",
        Edition = "The second, revised",
        EditionYear = 1769,
        About = "What this text is.",
        RightsNote = "What is unsettled about the rights.",
    };

    /// <summary>
    /// One book at canonical ordinal 8 but first in this text, so the two numbers cannot be
    /// mistaken for each other, and a trailer that carries punctuation and the space after it.
    /// </summary>
    private static TextSource Sample() => new(Definition, [
        new BookDraft(
            CanonicalOrdinal: 8,
            Position: 1,
            Name: "Ruth",
            Slug: "ruth",
            Chapters:
            [
                new ChapterDraft(1, [
                    new VerseDraft(1, [
                        new WordDraft("In", " "),
                        new WordDraft("the", " "),
                        new WordDraft("beginning", ", "),
                        new WordDraft("God", " "),
                    ]),
                    new VerseDraft(2, [new WordDraft("Amen", ".")]),
                ]),
                new ChapterDraft(2, [new VerseDraft(1, [new WordDraft("Selah", "")])]),
            ]),
    ]);
}
