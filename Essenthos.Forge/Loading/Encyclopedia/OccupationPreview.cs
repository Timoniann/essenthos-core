using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record OccupationPreviewPerson(string Slug, string Name, string? Sex, Dictionary<string, string> LocalNames);
internal sealed record OccupationPreviewPeople(IReadOnlyList<OccupationPreviewPerson> People);

/// <summary>Minimal source-bound reader fixtures, restricted to the disposable test server.</summary>
internal static class OccupationPreview
{
    internal static void Guard(string connectionString)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (connection.Port != 5439 || connection.Host is not ("localhost" or "127.0.0.1" or "::1")
            || connection.Database is not { } name || !name.StartsWith("essenthos_occupation_preview", StringComparison.Ordinal)
            || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            throw new InvalidOperationException("Occupation preview requires a local test server on 5439 and an essenthos_occupation_preview database.");
    }

    internal static async Task Seed(AppDbContext db, string publicationPath, string expectedSha256, string peoplePath,
        CancellationToken cancellationToken = default)
    {
        var connectionString = db.Database.GetConnectionString()!;
        Guard(connectionString);
        var bytes = await File.ReadAllBytesAsync(publicationPath, cancellationToken);
        var publication = OccupationLoader.Read(bytes, expectedSha256);
        var inventory = JsonSerializer.Deserialize<OccupationPreviewPeople>(await File.ReadAllBytesAsync(peoplePath, cancellationToken), OccupationLoader.Shape)
                        ?? throw new InvalidDataException("Occupation preview people inventory is empty.");
        if (inventory.People.Count != inventory.People.Select(p => p.Slug).Distinct().Count()
            || publication.Assertions.Any(a => inventory.People.All(p => p.Slug != a.PersonSlug))
            || inventory.People.Any(p => string.IsNullOrWhiteSpace(p.Name)))
            throw new InvalidDataException("Occupation preview needs one explicitly named person for every assertion.");

        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            var configuration = new NpgsqlConnectionStringBuilder(connectionString);
            var databaseName = configuration.Database;
            configuration.Database = "postgres";
            await using var server = new NpgsqlConnection(configuration.ConnectionString);
            await server.OpenAsync(cancellationToken);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", server);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
        await db.Database.MigrateAsync(cancellationToken);
        foreach (var person in inventory.People)
        {
            var entity = await db.Entities.SingleOrDefaultAsync(e => e.Slug == person.Slug, cancellationToken);
            if (entity is null)
            {
                entity = new Entity { Slug = person.Slug, Name = person.Name, Sex = person.Sex,
                    Kind = EntityKind.Person, Source = publication.Source.Attribution, SourceId = "occupation-preview:" + person.Slug };
                db.Entities.Add(entity);
                await db.SaveChangesAsync(cancellationToken);
            }
            foreach (var (language, name) in person.LocalNames)
                if (!await db.EntityNameForms.AnyAsync(f => f.EntityId == entity.Id && f.Language == language
                    && f.GrammaticalCase == GrammaticalCases.Nominative, cancellationToken))
                    db.EntityNameForms.Add(new EntityNameForm { EntityId = entity.Id, Language = language,
                        GrammaticalCase = GrammaticalCases.Nominative, Form = name, Source = publication.Source.Attribution });
            foreach (var evidence in publication.Assertions.Where(a => a.PersonSlug == person.Slug).SelectMany(a => a.Witnesses)
                .Select(w => OccupationLoader.Address(w.CanonicalReference)!.Value).Distinct())
                if (!await db.EntityVerses.AnyAsync(v => v.EntityId == entity.Id && v.CanonicalBook == evidence.Book
                    && v.CanonicalChapter == evidence.Chapter && v.CanonicalVerse == evidence.Verse, cancellationToken))
                    db.EntityVerses.Add(new EntityVerse { EntityId = entity.Id, CanonicalBook = evidence.Book,
                        CanonicalChapter = evidence.Chapter, CanonicalVerse = evidence.Verse,
                        Names = false, Source = publication.Source.Attribution });
            await db.SaveChangesAsync(cancellationToken);
        }
        foreach (var witness in publication.Assertions.SelectMany(a => a.Witnesses).DistinctBy(w => (w.Text, w.Reference)))
        {
            var address = OccupationLoader.Address(witness.Reference)!.Value;
            var canonical = OccupationLoader.Address(witness.CanonicalReference)!.Value;
            var text = await db.Texts.SingleOrDefaultAsync(t => t.Slug == witness.Text, cancellationToken);
            if (text is null)
            {
                text = new Text { Slug = witness.Text, Name = witness.Text, Language = witness.Text.ToUpperInvariant() switch
                    { "BHSA" => "hbo", "NESTLE1904" or "RP2018" or "TR1894" => "grc", "UBIO" => "ukr", "RUSV" => "rus", "LUTH1912" => "deu", "RV1909" => "spa", _ => "eng" },
                    Kind = witness.Text == "NESTLE1904" ? TextKind.CriticalEdition : witness.Text == "BHSA" ? TextKind.ManuscriptTradition : TextKind.Translation,
                    SourceUrl = publication.Source.Uri, Citation = publication.Source.Attribution, Licence = publication.Source.License };
                db.Texts.Add(text);
                await db.SaveChangesAsync(cancellationToken);
            }
            var book = await db.Books.SingleOrDefaultAsync(b => b.TextId == text.Id && b.CanonicalOrdinal == address.Book, cancellationToken);
            if (book is null)
            {
                book = new Book { TextId = text.Id, CanonicalOrdinal = address.Book, Position = address.Book,
                    Name = BookReferences.Name(address.Book), Slug = BookReferences.Slug(address.Book) };
                db.Books.Add(book);
                await db.SaveChangesAsync(cancellationToken);
            }
            var chapter = await db.Chapters.SingleOrDefaultAsync(c => c.BookId == book.Id && c.Number == address.Chapter, cancellationToken);
            if (chapter is null)
            {
                chapter = new Chapter { TextId = text.Id, BookId = book.Id, Number = address.Chapter };
                db.Chapters.Add(chapter);
                await db.SaveChangesAsync(cancellationToken);
            }
            if (await db.Verses.AnyAsync(v => v.ChapterId == chapter.Id && v.Number == address.Verse, cancellationToken)) continue;
            var verse = new Verse { TextId = text.Id, BookId = book.Id, ChapterId = chapter.Id,
                ChapterNumber = address.Chapter, Number = address.Verse, Sequence = address.Verse };
            db.Verses.Add(verse);
            await db.SaveChangesAsync(cancellationToken);
            var position = 0;
            var start = 0;
            while (start < witness.SourceText.Length)
            {
                var end = start;
                while (end < witness.SourceText.Length && !char.IsWhiteSpace(witness.SourceText[end])) end++;
                var next = end;
                while (next < witness.SourceText.Length && char.IsWhiteSpace(witness.SourceText[next])) next++;
                db.Words.Add(new Word { TextId = text.Id, VerseId = verse.Id, Position = ++position,
                    Surface = witness.SourceText[start..end], Trailer = witness.SourceText[end..next] });
                start = next;
            }
            db.VerseReferences.Add(new VerseReference { VerseId = verse.Id, CanonicalBook = canonical.Book,
                CanonicalChapter = canonical.Chapter, CanonicalVerse = canonical.Verse, IsPrimary = true });
            await db.SaveChangesAsync(cancellationToken);
        }
        await new OccupationLoader(db).Load(bytes, expectedSha256, cancellationToken);
    }
}
