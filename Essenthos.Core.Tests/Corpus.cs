using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Frame;
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
        return db.AddBook(text, 1, "Genesis", verses);
    }

    /// <summary>A further book of a text <see cref="Add"/> made, at its own place in the canon.</summary>
    public static Text AddBook(
        this AppDbContext db,
        Text text,
        int canonicalBook,
        string name,
        params (int Chapter, int Verse, string[] Words)[] verses)
    {
        var book = new Book
        {
            Text = text,
            CanonicalOrdinal = canonicalBook,
            Position = canonicalBook,
            Name = name,
            Slug = name.ToLowerInvariant()[..3],
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
                    CanonicalBook = canonicalBook,
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

    /// <summary>
    /// The words rulings name, each written at the address it names in its own text: the text is the
    /// one given for its slug or a new one, the book, chapter and verse are added where the text has
    /// none, and the positions before a ruled word are filled.
    /// </summary>
    public static Dictionary<RuledWord, Word> Place(
        this AppDbContext db,
        IEnumerable<RuledWord> words,
        IReadOnlyDictionary<string, Text>? texts = null)
    {
        var placed = new Dictionary<RuledWord, Word>();
        foreach (var verse in words.Distinct().GroupBy(word => (word.Text, Address: word.Address()!.Value)))
        {
            var (book, chapter, number, label) = verse.Key.Address;
            var text = texts?.GetValueOrDefault(verse.Key.Text)
                       ?? db.Texts.Local.FirstOrDefault(t => t.Slug == verse.Key.Text)
                       ?? db.Texts.Add(new Text
                       {
                           Slug = verse.Key.Text,
                           Name = verse.Key.Text,
                           Kind = verse.Key.Text == "BHSA" ? TextKind.CriticalEdition : TextKind.Translation,
                           Language = verse.Key.Text switch
                           {
                               "BHSA" => "hbo",
                               "NESTLE1904" or "GRCBRENT" => "grc",
                               _ => "eng",
                           },
                       }).Entity;
            var bookRow = db.Books.Local.FirstOrDefault(b => b.Text == text && b.CanonicalOrdinal == book)
                          ?? db.Books.Add(new Book
                          {
                              Text = text, CanonicalOrdinal = book, Position = book, Name = $"Book {book}", Slug = $"b{book}",
                          }).Entity;
            var chapterRow = db.Chapters.Local.FirstOrDefault(c => c.Book == bookRow && c.Number == chapter)
                             ?? db.Chapters.Add(new Chapter { Text = text, Book = bookRow, Number = chapter }).Entity;
            var verseRow = db.Verses.Add(new Verse
            {
                Text = text, Book = bookRow, Chapter = chapterRow, ChapterNumber = chapter, Number = number, Label = label,
            }).Entity;
            db.VerseReferences.Add(new VerseReference
            {
                Verse = verseRow, CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = number, IsPrimary = true,
            });

            var last = verse.Max(word => word.Position);
            for (var position = 1; position <= last; position++)
            {
                var ruled = verse.FirstOrDefault(word => word.Position == position);
                var word = db.Words.Add(new Word
                {
                    Text = text,
                    Verse = verseRow,
                    Position = position,
                    Surface = ruled?.Surface ?? $"word{position}",
                    Trailer = position == last ? string.Empty : " ",
                }).Entity;
                if (ruled is not null)
                {
                    placed[ruled] = word;
                }
            }
        }

        db.SaveChanges();
        return placed;
    }

    /// <summary>One word by its address, which is how a link's ends are named in a test.</summary>
    public static Word WordAt(this AppDbContext db, Text text, int chapter, int verse, int position) =>
        db.Words.Single(w =>
            w.TextId == text.Id &&
            w.Verse!.ChapterNumber == chapter &&
            w.Verse.Number == verse &&
            w.Position == position);

    /// <summary>A word's address, the way a stored answer or ruling names it.</summary>
    public static RuledWord Address(this AppDbContext db, long wordId)
    {
        var word = db.Words.Include(w => w.Text).Include(w => w.Verse).ThenInclude(v => v!.Book).Single(w => w.Id == wordId);
        return new RuledWord(
            word.Text!.Slug,
            $"{BookCodes.Code(word.Verse!.Book!.CanonicalOrdinal)} {word.Verse.ChapterNumber}:{word.Verse.Number}{word.Verse.Label}",
            word.Position,
            word.Surface);
    }

    /// <summary>The four fields an answer names its word by, to stand in its JSON.</summary>
    public static Dictionary<string, object?> AddressFields(this AppDbContext db, long wordId)
    {
        var address = db.Address(wordId);
        return new Dictionary<string, object?>
        {
            ["text"] = address.Text, ["reference"] = address.Reference,
            ["position"] = address.Position, ["surface"] = address.Surface,
        };
    }

    public static Verse VerseAt(this AppDbContext db, Text text, int chapter, int verse) =>
        db.Verses.Single(v => v.TextId == text.Id && v.ChapterNumber == chapter && v.Number == verse);

    /// <summary>The words on one side of a link, in no particular order — a link names a set.</summary>
    public static List<Word> Side(this AppDbContext db, long linkId, LinkSide side) =>
        db.LinkWords
            .Where(lw => lw.LinkId == linkId && lw.Side == side)
            .Select(lw => lw.Word!)
            .ToList();
}
