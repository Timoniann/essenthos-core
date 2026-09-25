using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The passes that put right what a warm corpus holds about its source words, in place: BHSA's
/// headwords and the Strong numbers the Berean's tables state.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SourceWordPassTests : IDisposable
{
    private readonly AppDbContext _db;

    public SourceWordPassTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    [Fact]
    public async Task BhsaLemmasBecomeHeadwordsInPlaceAndKeepTheOccurrencesSpelling()
    {
        var text = Corpus.Add(_db, BhsaTextSource.Slug, TextKind.CriticalEdition, "hbo", (1, 1, ["אֱלֹהִים", "הַ"]));
        _db.SaveChanges();
        var words = _db.Words.Where(w => w.TextId == text.Id).OrderBy(w => w.Position).ToList();
        words[0].Lemma = "אֱלֹה";
        words[0].Morphology = System.Text.Json.JsonDocument.Parse("""{"vocalizedLexeme":"אֱלֹהִים"}""");
        words[1].Morphology = System.Text.Json.JsonDocument.Parse("""{"vocalizedLexeme":"הַ"}""");
        _db.SaveChanges();
        var ids = words.Select(w => w.Id).ToList();

        var loader = new BhsaLemmaLoader(_db, NullLogger<BhsaLemmaLoader>.Instance);
        (await loader.Load()).Words.Should().Be(2);

        var after = _db.Words.AsNoTracking().Where(w => w.TextId == text.Id).OrderBy(w => w.Position).ToList();
        after.Select(w => w.Id).Should().Equal(ids);
        after.Select(w => w.Lemma).Should().Equal("אֱלֹהִים", "הַ");
        after[0].Morphology!.RootElement.GetProperty("realisedLexeme").GetString().Should().Be("אֱלֹה");
        after[1].Morphology!.RootElement.TryGetProperty("realisedLexeme", out _).Should().BeFalse();

        (await loader.Load()).Words.Should().Be(0);
    }

    [Fact]
    public async Task TheBereanCarriesTheNumbersItsTablesStateOnEveryWordOfTheirPhrase()
    {
        var text = Corpus.Add(_db, BereanTextSource.Slug, TextKind.Translation, "eng",
            (1, 1, ["In", "the", "beginning", "God", "created"]));
        _db.SaveChanges();
        var tables = Path.Combine(Path.GetTempPath(), $"bsb-tables-{Guid.NewGuid():N}.tsv");
        File.WriteAllLines(tables,
        [
            "header",
            Row(1, 1, "בְּרֵאשִׁית", "07225", "Genesis 1:1", "In the beginning"),
            Row(2, 3, "בָּרָא", "01254", "", "created"),
            Row(3, 2, "אֱלֹהִים", "0430", "", "God"),
            Row(4, 4, "אֵת", "0853", "", " -"),
        ]);

        try
        {
            var loader = new BereanNumberLoader(_db, NullLogger<BereanNumberLoader>.Instance);
            var outcome = await loader.Load(tables);

            outcome.Words.Should().Be(5);
            var numbers = _db.WordStrongs.AsNoTracking()
                .Where(s => s.Word!.TextId == text.Id)
                .OrderBy(s => s.Word!.Position)
                .Select(s => new { s.Word!.Surface, s.Number, s.Method, s.Confidence, s.Source })
                .ToList();
            numbers.Select(n => $"{n.Surface} {n.Number}").Should().Equal(
                "In H7225", "the H7225", "beginning H7225", "God H430", "created H1254");
            numbers.Should().OnlyContain(n => n.Method == LinkMethod.StatedBySource && n.Confidence == null
                                              && n.Source == BereanNumberLoader.Source);

            (await loader.Load(tables)).AlreadyNumbered.Should().BeTrue();
        }
        finally
        {
            File.Delete(tables);
        }
    }

    private static string Row(double order, int english, string hebrew, string strong, string reference, string rendering)
    {
        var cells = Enumerable.Repeat(string.Empty, 19).ToArray();
        cells[0] = order.ToString(System.Globalization.CultureInfo.InvariantCulture);
        cells[1] = "0";
        cells[2] = english.ToString(System.Globalization.CultureInfo.InvariantCulture);
        cells[3] = "1";
        cells[4] = "Hebrew";
        cells[5] = hebrew;
        cells[6] = hebrew;
        cells[10] = strong;
        cells[12] = reference;
        cells[18] = rendering;
        return string.Join('\t', cells);
    }
}
