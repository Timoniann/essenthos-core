using Essenthos.Core.Conversion;
using Essenthos.Core.Database;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Tests;

/// <summary>
/// The renderings a build counts, counted for a test's database: its texts, words and links written out as the products the
/// build reads (each text's declaration, words, books and frame, each pair's final), the renderings indexes made from them
/// (<see cref="RenderingIndex"/>), or the Strong and occurrences indexes (<see cref="TextIndexes.Strong"/>), and loaded back into the same
/// database on its connection and transaction.
/// </summary>
internal static class CountedRenderings
{
    public static Task Count(AppDbContext db) => Count(db, strong: false);

    /// <summary>The Strong indexes of every text, loaded into <c>strong_rendering</c>, <c>strong_rendering_place</c> and the rest.</summary>
    public static Task CountStrong(AppDbContext db) => Count(db, strong: true);

    private static async Task Count(AppDbContext db, bool strong)
    {
        await db.SaveChangesAsync();
        var resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProductStore(resources);
            var texts = await db.Texts.AsNoTracking().ToListAsync();
            var slugs = texts.ToDictionary(t => t.Id, t => t.Slug);
            var keys = new Dictionary<long, WordKey>();
            foreach (var text in texts)
            {
                using (var writer = store.Create(TextField.ProductOf(text.Slug), ProductKind.Hand, TextField.Schema, "test", []))
                {
                    writer.WriteAll([
                        new TextField("slug", text.Slug), new TextField("kind", EnumSpelling.Of(text.Kind)), new TextField("language", text.Language),
                    ]);
                }

                var books = await db.Books.AsNoTracking().Where(b => b.TextId == text.Id).OrderBy(b => b.CanonicalOrdinal).ToListAsync();
                using (var writer = store.Create(TextBook.ProductOf(text.Slug), ProductKind.Machine, TextBook.Schema, "test", []))
                {
                    writer.WriteAll(books.Select(b => new TextBook(b.CanonicalOrdinal, b.Position, b.Name, null, null, b.Slug)));
                }

                var words = await db.Words.AsNoTracking()
                    .Where(w => w.TextId == text.Id)
                    .Select(w => new
                    {
                        w.Id, Book = w.Verse!.Book!.CanonicalOrdinal, w.Verse.ChapterNumber, w.Verse.Number, w.Verse.Label, w.Position,
                        w.Surface, w.Trailer, w.Lemma, w.StrongNumber, w.NormalisedText, w.Elided,
                    })
                    .ToListAsync();
                foreach (var word in words)
                {
                    keys[word.Id] = new WordKey(text.Slug, new VerseKey(word.Book, word.ChapterNumber, word.Number, word.Label), word.Position);
                }

                using (var writer = store.Create(TextWord.ProductOf(text.Slug), ProductKind.Machine, TextWord.Schema(text.Slug), "test", []))
                {
                    writer.WriteAll(words.OrderBy(w => keys[w.Id]).Select(w => new TextWord(
                        keys[w.Id], w.Surface, w.Trailer, w.Lemma, w.StrongNumber, null, null, w.NormalisedText, null, w.Elided, null, null, null)));
                }

                var placed = await db.VerseReferences.AsNoTracking()
                    .Where(r => r.Verse!.TextId == text.Id && r.IsPrimary)
                    .Select(r => new
                    {
                        Book = r.Verse!.Book!.CanonicalOrdinal, r.Verse.ChapterNumber, r.Verse.Number, r.Verse.Label,
                        r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse,
                    })
                    .ToListAsync();
                using (var writer = store.Create(FrameFile.Product(text.Slug), ProductKind.Machine, FrameRow.Schema, "test", []))
                {
                    writer.WriteAll(placed
                        .Select(r => (Own: new VerseKey(r.Book, r.ChapterNumber, r.Number, r.Label), At: new VerseKey(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse)))
                        .Where(r => r.At != FrameFile.AtOwn(r.Own))
                        .OrderBy(r => r.Own)
                        .Select(r => new FrameRow(r.Own, null, [r.At], "moved")));
                }
            }

            var links = await db.Links.AsNoTracking()
                .Select(l => new
                {
                    l.Id, l.FromTextId, l.ToTextId, l.Relation, l.Method, l.Confidence, Source = l.Provenance!.Source,
                    Words = l.Words.Select(w => new { w.WordId, w.Side }).ToList(),
                })
                .ToListAsync();
            foreach (var pair in links.GroupBy(l => (l.FromTextId, l.ToTextId)))
            {
                var name = MappingProducts.Pair(slugs[pair.Key.FromTextId], slugs[pair.Key.ToTextId]);
                using var writer = store.Create(MappingProducts.Of(name, MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []);
                writer.WriteAll(pair.OrderBy(l => l.Id).Select(l => new FinalRow(
                    l.Id,
                    MappingText.Keys(l.Words.Where(w => w.Side == Database.Entities.Enums.LinkSide.From).Select(w => keys[w.WordId])),
                    MappingText.Keys(l.Words.Where(w => w.Side == Database.Entities.Enums.LinkSide.To).Select(w => keys[w.WordId])),
                    EnumSpelling.Of(l.Relation),
                    null,
                    true,
                    MappingMethods.Classify(EnumSpelling.Of(l.Method), l.Source ?? string.Empty),
                    l.Confidence,
                    l.Source ?? string.Empty)));
            }

            store.Save();
            var context = new IndexContext(store);
            foreach (var text in texts)
            {
                if (strong)
                {
                    TextIndexes.Strong(context, text.Slug, force: true);
                    TextIndexes.Occurrences(context, text.Slug, force: true);
                }
                else
                {
                    RenderingIndex.Make(context, text.Slug, force: true);
                }
            }

            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            if (strong)
            {
                await IndexLoad.Strong(connection, store, [.. texts.Select(t => t.Slug)], CancellationToken.None);
            }
            else
            {
                await IndexLoad.WordRenderings(connection, store, [.. texts.Select(t => t.Slug)], CancellationToken.None);
            }
        }
        finally
        {
            if (Directory.Exists(resources))
            {
                Directory.Delete(resources, recursive: true);
            }
        }
    }
}
