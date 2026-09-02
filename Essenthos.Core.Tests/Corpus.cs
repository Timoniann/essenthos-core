using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Tests;

/// <summary>
/// A few verses of a text, enough to hang links on. Every word gets a trailing space so that a
/// verse read back by concatenation is the sentence it was written as.
/// </summary>
internal static class Corpus
{
    public static Text Add(
        AppDbContext db,
        string slug,
        TextKind kind,
        string language,
        params (int Chapter, int Verse, string[] Words)[] verses)
    {
        var text = new Text
        {
            Slug = slug,
            Name = slug,
            Kind = kind,
            Language = language,
        };
        db.Texts.Add(text);

        var book = new Book
        {
            Text = text,
            CanonicalOrdinal = 1,
            Position = 1,
            Name = "Genesis",
            Slug = "gen",
        };
        db.Books.Add(book);

        foreach (var chapterNumber in verses.Select(v => v.Chapter).Distinct())
        {
            var chapter = new Chapter { Text = text, Book = book, Number = chapterNumber };
            db.Chapters.Add(chapter);

            foreach (var (_, verseNumber, words) in verses.Where(v => v.Chapter == chapterNumber))
            {
                var verse = new Verse
                {
                    Text = text,
                    Book = book,
                    Chapter = chapter,
                    ChapterNumber = chapterNumber,
                    Number = verseNumber,
                };
                db.Verses.Add(verse);
                db.VerseReferences.Add(new VerseReference
                {
                    Verse = verse,
                    CanonicalBook = 1,
                    CanonicalChapter = chapterNumber,
                    CanonicalVerse = verseNumber,
                    IsPrimary = true,
                });

                for (var position = 0; position < words.Length; position++)
                {
                    db.Words.Add(new Word
                    {
                        Text = text,
                        Verse = verse,
                        Position = position + 1,
                        Surface = words[position],
                        Trailer = position == words.Length - 1 ? string.Empty : " ",
                    });
                }
            }
        }

        return text;
    }

    /// <summary>
    /// Moves a text into another part of the canonical frame. <see cref="Add"/> puts everything in
    /// Genesis, which is right until the test is about the two halves of the canon disagreeing.
    /// </summary>
    public static Text In(this AppDbContext db, Text text, int canonicalBook)
    {
        foreach (var book in db.Books.Where(b => b.TextId == text.Id))
        {
            book.CanonicalOrdinal = canonicalBook;
        }

        foreach (var reference in db.VerseReferences.Where(r => r.Verse!.TextId == text.Id))
        {
            reference.CanonicalBook = canonicalBook;
        }

        db.SaveChanges();
        return text;
    }

    /// <summary>One word by its address, which is how a link's ends are named in a test.</summary>
    public static Word WordAt(this AppDbContext db, Text text, int chapter, int verse, int position) =>
        db.Words.Single(w =>
            w.TextId == text.Id &&
            w.Verse!.ChapterNumber == chapter &&
            w.Verse.Number == verse &&
            w.Position == position);

    public static Verse VerseAt(this AppDbContext db, Text text, int chapter, int verse) =>
        db.Verses.Single(v => v.TextId == text.Id && v.ChapterNumber == chapter && v.Number == verse);

    /// <summary>The words on one side of a link, in no particular order — a link names a set.</summary>
    public static List<Word> Side(this AppDbContext db, long linkId, LinkSide side) =>
        db.LinkWords
            .Where(lw => lw.LinkId == linkId && lw.Side == side)
            .Select(lw => lw.Word!)
            .ToList();
}
