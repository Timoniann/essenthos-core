using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The records this corpus writes for itself, exercised against the rulings it actually ships
/// rather than against a fixture standing in for them.
///
/// The words are written at the addresses the ruling files name, so what is under test is the file a
/// reader would be shown from — a ruling whose address is malformed, or whose referent slug was
/// renamed away, fails here rather than quietly annotating nothing on a live database.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyList<OwnRecordRuling> _rulings;
    private readonly Dictionary<RuledWord, long> _words;
    private readonly Text _english;

    public OwnRecordTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _rulings = [.. SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)];

        // The words the rulings are about, at the addresses the rulings name.
        var placed = _db.Place(_rulings.Select(ruling => ruling.Word));
        foreach (var ruling in _rulings)
        {
            placed[ruling.Word].StrongNumber = ruling.StrongNumber;
            placed[ruling.Word].Morphology = JsonDocument.Parse("""{"pos": "subs", "nameType": "pers"}""");
        }

        _english = Corpus.Add(_db, "ENGLISH", TextKind.Translation, "eng", (1, 1, ["Azariah"]));
        _db.SaveChanges();
        _words = placed.ToDictionary(word => word.Key, word => word.Value.Id);

        // The records the rulings point at or name as alternatives.
        foreach (var slug in Named())
        {
            _db.Entities.Add(new Entity
            {
                Kind = slug == "anointed" ? EntityKind.Title : EntityKind.Person,
                Slug = slug,
                Name = slug,
                SourceId = slug,
                Source = "a test",
            });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    /// <summary>Every record a ruling refers to and does not create.</summary>
    private IEnumerable<string> Named() =>
        _rulings
            .SelectMany(r => new[] { r.Existing }
                .Concat(r.Alternatives?.Select(a => a.Slug) ?? []))
            .Where(slug => slug is not null)
            .Select(slug => slug!)
            .Where(slug => _rulings.All(r => r.Create?.Slug != slug))
            .Distinct();

    private OwnRecordLoader Loader(bool bulk = false) =>
        new(
            _db,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [OwnRecordLoader.BulkConfigurationKey] = bulk ? "true" : "false",
                })
                .Build(),
            NullLogger<OwnRecordLoader>.Instance);

    private Task<OwnRecordOutcome> Load() =>
        Loader().Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

    /// <summary>
    /// On a corpus built from nothing the titles are written after these rulings, so the Anointed is
    /// not held yet when the Synodal's companion ruling is checked. The owner decided it is a title,
    /// and that is what the ruling beside it rests on.
    /// </summary>
    [Fact]
    public async Task ACompanionRulingStandsBesideATitleTheOwnerDecidedBeforeTheTitleIsWritten()
    {
        await _db.Entities.Where(e => e.Slug == "anointed").ExecuteDeleteAsync();
        _db.ChangeTracker.Clear();

        var loading = async () => await Load();

        await loading.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SynodalAddedChristNamesTheTitleAndJesusLocallyWithoutChangingSatan()
    {
        var address = new RuledWord("RUSV", "1CO 5:5", 15, "Христа");
        var christ = _words.TryGetValue(address, out var id) ? _db.Words.Single(w => w.Id == id)
            : _db.Place([address])[address];
        var russianSatan = _db.Words.Single(w => w.VerseId == christ.VerseId && w.Position == 2);
        russianSatan.Surface = "сатане";
        var greekAddress = new RuledWord("NESTLE1904", "1CO 5:5", 5, "Σατανᾷ");
        var greek = _db.Place([greekAddress])[greekAddress];
        foreach (var slug in new[] { "satan", "jesus", "anointed" })
            if (!_db.Entities.Any(e => e.Slug == slug))
                _db.Entities.Add(new Entity { Slug = slug, SourceId = slug, Name = slug,
                    Kind = slug == "anointed" ? EntityKind.Title : EntityKind.Person, Source = "a test" });
        await _db.SaveChangesAsync();
        var satan = await _db.Entities.SingleAsync(e => e.Slug == "satan");
        _db.TitleBearers.Add(new TitleBearer {
            TitleEntityId = (await _db.Entities.SingleAsync(e => e.Slug == "anointed")).Id,
            BearerEntityId = (await _db.Entities.SingleAsync(e => e.Slug == "jesus")).Id,
            CanonicalBook = 43, CanonicalChapter = 1, CanonicalVerse = 41,
            Source = "a fixture of the held Christ-Jesus title/bearer relation" });
        foreach (var word in new[] { russianSatan, greek })
            _db.WordEntities.Add(new() { Word = word, Entity = satan, Method = LinkMethod.StrongNumber,
                Confidence = 0.99, Source = "independent Satan reading" });
        var link = new Link { FromTextId = christ.TextId, ToTextId = greek.TextId,
            Method = LinkMethod.Aligner, Relation = LinkRelation.Renders, Confidence = 0.29,
            Provenance = new() { Source = "a fixture's faint alignment" } };
        link.Words.Add(new() { Word = christ, Side = LinkSide.From });
        link.Words.Add(new() { Word = greek, Side = LinkSide.To });
        await _db.SaveChangesAsync();
        var satanIds = await _db.WordEntities.Where(a => a.EntityId == satan.Id).Select(a => a.Id).ToArrayAsync();

        await Load();
        var named = await _db.WordEntities.Where(a => a.WordId == christ.Id).Include(a => a.Entity).AsNoTracking().ToListAsync();
        named.Select(a => a.Entity!.Slug).Should().BeEquivalentTo("anointed", "jesus");
        (await Essenthos.Core.Corpus.Annotations.AllOf(_db, [christ.Id], CancellationToken.None))[christ.Id]
            .Select(e => e.Slug).Should().BeEquivalentTo("anointed", "jesus");
        named.Should().OnlyContain(a => a.Method == LinkMethod.Manual && a.Confidence == null);
        var ids = named.Select(a => a.Id).Order().ToArray();
        var carrier = new AnnotationCarrier(_db,
            new CrossedNameLoader(_db, NullLogger<CrossedNameLoader>.Instance),
            new ForeignNames(_db, NullLogger<ForeignNames>.Instance), NullLogger<AnnotationCarrier>.Instance);
        await carrier.Carry();
        (await _db.WordEntities.Where(a => a.WordId == greek.Id).Select(a => a.EntityId).ToArrayAsync())
            .Should().Equal(satan.Id);
        (await _db.WordEntities.Where(a => a.EntityId == satan.Id && (a.WordId == russianSatan.Id || a.WordId == greek.Id)).Select(a => a.Id).ToArrayAsync())
            .Should().BeEquivalentTo(satanIds);
        var references = new OwnReferenceLoader(_db, NullLogger<OwnReferenceLoader>.Instance);
        await references.Load();
        await _db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        foreach (var slug in new[] { "jesus", "anointed" })
            (await _db.EntityVerses.AnyAsync(v => v.Entity!.Slug == slug && v.CanonicalBook == 46
                && v.CanonicalChapter == 5 && v.CanonicalVerse == 5 && v.Names)).Should().BeTrue();
        await Load();
        (await _db.WordEntities.Where(a => a.WordId == christ.Id && a.EntityId != satan.Id)
            .OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync()).Should().Equal(ids);
        await _db.WordEntities.Where(a => a.WordId == christ.Id && a.EntityId != satan.Id).ExecuteDeleteAsync();
        await Load();
        (await _db.WordEntities.Where(a => a.WordId == christ.Id && a.EntityId != satan.Id)
            .Include(a => a.Entity).Select(a => a.Entity!.Slug).ToArrayAsync()).Should().BeEquivalentTo("anointed", "jesus");
        (await _db.WordEntities.Where(a => a.WordId == greek.Id).Select(a => a.EntityId).ToArrayAsync())
            .Should().Equal(satan.Id);
    }

    [Fact]
    public async Task UkrainianAdamNamesAdamAndPreservesHisWifesReading()
    {
        var adamAddress = new RuledWord("UBIO", "GEN 3:8", 17, "Адам");
        var wifeAddress = new RuledWord("UBIO", "GEN 3:8", 20, "жінка");
        var addresses = new[] { adamAddress, wifeAddress };
        var added = _words.ContainsKey(adamAddress)
            ? new Dictionary<RuledWord, Word>() : _db.Place(addresses);
        if (_words.TryGetValue(adamAddress, out var adamWordId))
        {
            var adamWord = await _db.Words.SingleAsync(w => w.Id == adamWordId);
            added[wifeAddress] = _db.Words.Add(new Word { TextId = adamWord.TextId, VerseId = adamWord.VerseId,
                Position = wifeAddress.Position, Surface = wifeAddress.Surface, Trailer = " " }).Entity;
            await _db.SaveChangesAsync();
        }
        var focus = addresses.ToDictionary(a => a, a => _words.TryGetValue(a, out var id)
            ? _db.Words.Single(w => w.Id == id) : added[a]);
        foreach (var slug in new[] { "adam", "eve" })
            if (!_db.Entities.Any(e => e.Slug == slug))
                _db.Entities.Add(new Entity { Slug = slug, SourceId = slug, Name = slug,
                    Kind = EntityKind.Person, Source = "a test" });
        await _db.SaveChangesAsync();
        var adam = await _db.Entities.SingleAsync(e => e.Slug == "adam");
        var eve = await _db.Entities.SingleAsync(e => e.Slug == "eve");
        var wrong = new WordEntity { Word = focus[adamAddress], Entity = eve,
            Method = LinkMethod.RuleBased, Confidence = 0.988028802880288, Source = NameConsensusPass.Source,
            Note = "the word its verses share in this text: адам" };
        var wife = new WordEntity { Word = focus[wifeAddress], Entity = eve,
            Method = LinkMethod.ModelReading, Confidence = 0.91, Source = "a reading of his wife",
            Claims = [new WordEntityClaim { Method = LinkMethod.ModelReading, Confidence = 0.91,
                Source = "a reading of his wife" }] };
        _db.WordEntities.AddRange(wrong, wife);
        await _db.SaveChangesAsync();
        var wifeId = wife.Id;
        var wifeClaimId = wife.Claims.Single().Id;

        await Load();
        var wordId = focus[adamAddress].Id;
        (await _db.WordEntities.Where(a => a.WordId == wordId).Select(a => a.EntityId).ToArrayAsync())
            .Should().Equal(adam.Id);
        (await _db.WordEntities.AsNoTracking().SingleAsync(a => a.Id == wifeId)).EntityId.Should().Be(eve.Id);
        (await _db.WordEntityClaims.AsNoTracking().SingleAsync(c => c.Id == wifeClaimId)).WordEntityId.Should().Be(wifeId);
        var fixedId = (await _db.WordEntities.SingleAsync(a => a.WordId == wordId)).Id;
        await Load();
        (await _db.WordEntities.SingleAsync(a => a.WordId == wordId)).Id.Should().Be(fixedId);
        await _db.WordEntities.Where(a => a.WordId == wordId).ExecuteDeleteAsync();
        await Load();
        (await _db.WordEntities.AsNoTracking().SingleAsync(a => a.WordId == wordId)).EntityId.Should().Be(adam.Id);
    }

    [Fact]
    public async Task RevelationTribalNamesIdentifyTheAncestorsAndKeepNamesakesSeparate()
    {
        var addresses = new[] {
            (new RuledWord("NESTLE1904", "REV 7:4", 16, "Ἰσραήλ"), "jacob"),
            (new RuledWord("NESTLE1904", "REV 7:5", 3, "Ἰούδα"), "judah"),
            (new RuledWord("NESTLE1904", "REV 7:6", 8, "Νεφθαλεὶμ"), "naphtali"),
            (new RuledWord("NESTLE1904", "REV 7:6", 13, "Μανασσῆ"), "manasseh"),
            (new RuledWord("BSB", "REV 7:4", 18, "Israel"), "jacob"),
            (new RuledWord("BSB", "REV 7:5", 5, "Judah"), "judah"),
            (new RuledWord("BSB", "REV 7:6", 12, "Naphtali"), "naphtali"),
            (new RuledWord("BSB", "REV 7:6", 19, "Manasseh"), "manasseh"),
        };
        var added = _db.Place(addresses.Select(a => a.Item1).Where(a => !_words.ContainsKey(a)));
        var focus = addresses.ToDictionary(a => a.Item1, a => _words.TryGetValue(a.Item1, out var id)
            ? _db.Words.Single(w => w.Id == id) : added[a.Item1]);
        foreach (var slug in addresses.Select(a => a.Item2).Concat(new[] { "manasseh-3", "judah-6", "israelites" }).Distinct())
            if (!_db.Entities.Any(e => e.Slug == slug))
                _db.Entities.Add(new Entity { Slug = slug, SourceId = slug, Name = slug,
                    Kind = slug == "judah-6" ? EntityKind.Place : slug == "israelites" ? EntityKind.People : EntityKind.Person,
                    Source = "a test" });
        await _db.SaveChangesAsync();
        var entities = await _db.Entities.ToDictionaryAsync(e => e.Slug);
        foreach (var (address, target) in addresses.Where(a => a.Item2 != "naphtali"))
            _db.WordEntities.Add(new WordEntity { Word = focus[address], Entity = entities[target == "jacob"
                ? "israelites" : target == "judah" ? "judah-6" : "manasseh-3"],
                Method = LinkMethod.ModelReading, Confidence = 0.99, Source = "a fixture of the old readings" });
        var separate = _db.Place([new RuledWord("BSB", "MAT 1:10", 1, "Manasseh")]).Values.Single();
        _db.WordEntities.Add(new WordEntity { Word = separate, Entity = entities["manasseh-3"],
            Method = LinkMethod.Manual, Source = "a test of the king in the genealogy" });
        await _db.SaveChangesAsync();

        await Load();
        foreach (var (address, target) in addresses)
            (await _db.WordEntities.Where(a => a.WordId == focus[address].Id).Select(a => a.Entity!.Slug).ToArrayAsync())
                .Should().Equal(target);
        (await _db.WordEntities.SingleAsync(a => a.WordId == separate.Id)).EntityId.Should().Be(entities["manasseh-3"].Id);
        var references = new OwnReferenceLoader(_db, NullLogger<OwnReferenceLoader>.Instance);
        await references.Load();
        await _db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        (await _db.EntityVerses.AnyAsync(v => v.EntityId == entities["naphtali"].Id && v.CanonicalBook == 66
            && v.CanonicalChapter == 7 && v.CanonicalVerse == 6 && v.Names)).Should().BeTrue();
        var ids = focus.Values.Select(w => w.Id).ToArray();
        var before = await _db.WordEntities.Where(a => ids.Contains(a.WordId)).OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync();
        await Load();
        (await _db.WordEntities.Where(a => ids.Contains(a.WordId)).OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync())
            .Should().Equal(before);
        await _db.WordEntities.Where(a => a.WordId == focus[addresses[2].Item1].Id).ExecuteDeleteAsync();
        await Load();
        (await _db.WordEntities.SingleAsync(a => a.WordId == focus[addresses[2].Item1].Id)).EntityId
            .Should().Be(entities["naphtali"].Id);
    }
    [Fact]
    public async Task EveryRulingAnnotatesItsWord()
    {
        await Load();

        var named = await _db.WordEntities.Include(a => a.Entity).ToListAsync();
        named.Select(a => a.WordId).Distinct().Should().BeEquivalentTo(_rulings.Select(r => _words[r.Word]).Distinct());
        named.Should().OnlyContain(a => a.Method == LinkMethod.Manual);
        named.Should().OnlyContain(a => a.Confidence == null);
    }

    [Fact]
    public async Task SynodalJehoiakimNamesTheKingWithoutAddingHimToGreekJeconiah()
    {
        var addresses = new[] {
            new RuledWord("RUSV", "MAT 1:11", 3, "Иоакима"),
            new RuledWord("RUSV", "MAT 1:11", 4, "Иоаким"),
        };
        var missing = _db.Place(addresses.Where(a => !_words.ContainsKey(a)));
        var source = addresses.Select(a => _words.TryGetValue(a, out var id)
            ? _db.Words.Single(w => w.Id == id) : missing[a]).ToArray();
        var others = _db.Place([
            new RuledWord("NESTLE1904", "MAT 1:11", 5, "Ἰεχονίαν"),
            new RuledWord("RUSV", "MAT 1:13", 6, "Елиакима"),
        ]);
        var greek = others.Single(p => p.Key.Text == "NESTLE1904").Value;
        var namesake = others.Single(p => p.Key.Text == "RUSV").Value;
        foreach (var slug in new[] { "eliakim-2", "jehoiachin", "eliakim-4" })
            if (!_db.Entities.Any(e => e.Slug == slug))
                _db.Entities.Add(new Entity { Slug = slug, SourceId = slug, Name = slug,
                    Kind = EntityKind.Person, Source = "a test" });
        await _db.SaveChangesAsync();
        var king = await _db.Entities.SingleAsync(e => e.Slug == "eliakim-2");
        var jeconiah = await _db.Entities.SingleAsync(e => e.Slug == "jehoiachin");
        var otherEliakim = await _db.Entities.SingleAsync(e => e.Slug == "eliakim-4");
        foreach (var pair in new[] { (greek, jeconiah), (namesake, otherEliakim) })
            _db.WordEntities.Add(new WordEntity { Word = pair.Item1, Entity = pair.Item2,
                Method = LinkMethod.Manual, Source = "a test's independent name reading" });
        var link = new Link { FromTextId = source[0].TextId, ToTextId = greek.TextId,
            Method = LinkMethod.Aligner, Relation = LinkRelation.Renders, Confidence = 0.29,
            Provenance = new() { Source = "a fixture's faint alignment" } };
        _db.LinkWords.Add(new LinkWord { Link = link, Word = source[0], Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = greek, Side = LinkSide.To });
        await _db.SaveChangesAsync();

        await Load();
        var ids = source.Select(w => w.Id).ToArray();
        var named = await _db.WordEntities.Where(a => ids.Contains(a.WordId)).ToListAsync();
        named.Should().HaveCount(2).And.OnlyContain(a => a.EntityId == king.Id);
        named.Should().OnlyContain(a => a.Method == LinkMethod.Manual && a.Confidence == null);
        (await _db.WordEntities.AnyAsync(a => a.WordId == greek.Id && a.EntityId == king.Id))
            .Should().BeFalse();
        (await _db.WordEntities.SingleAsync(a => a.WordId == namesake.Id)).EntityId.Should().Be(otherEliakim.Id);
        var carrier = new AnnotationCarrier(_db,
            new CrossedNameLoader(_db, NullLogger<CrossedNameLoader>.Instance),
            new ForeignNames(_db, NullLogger<ForeignNames>.Instance),
            NullLogger<AnnotationCarrier>.Instance);
        await carrier.Carry();
        (await _db.WordEntities.AnyAsync(a => a.WordId == greek.Id && a.EntityId == king.Id))
            .Should().BeFalse();
        var references = new OwnReferenceLoader(_db, NullLogger<OwnReferenceLoader>.Instance);
        await references.Load();
        await _db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        (await _db.EntityVerses.AnyAsync(v => v.EntityId == king.Id && v.CanonicalBook == 40
            && v.CanonicalChapter == 1 && v.CanonicalVerse == 11 && v.Names)).Should().BeTrue();
        var annotationIds = named.Select(a => a.Id).Order().ToArray();
        await Load();
        (await _db.WordEntities.Where(a => ids.Contains(a.WordId) && a.EntityId == king.Id).OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync())
            .Should().Equal(annotationIds);
        await _db.WordEntities.Where(a => a.WordId == source[0].Id && a.EntityId == king.Id).ExecuteDeleteAsync();
        await Load();
        (await _db.WordEntities.AnyAsync(a => a.WordId == source[0].Id && a.EntityId == king.Id))
            .Should().BeTrue();
        (await _db.WordEntities.AnyAsync(a => a.WordId == greek.Id && a.EntityId == king.Id))
            .Should().BeFalse();
    }

    [Fact]
    public async Task PhilipInTheReceivedTextNamesHerodiasHusbandAndKeepsTheTetrarchSeparate()
    {
        var sourceAddress = new RuledWord("TR1894", "LUK 3:19", 13, "φιλιππου");
        var source = _words.TryGetValue(sourceAddress, out var sourceId)
            ? await _db.Words.SingleAsync(w => w.Id == sourceId)
            : _db.Place([sourceAddress])[sourceAddress];
        var targets = _db.Place([
            new RuledWord("TR1550", "LUK 3:19", 13, "φιλιππου"),
            new RuledWord("KJV", "LUK 3:19", 13, "Philip's"),
            new RuledWord("TR1550", "LUK 3:1", 19, "φιλιππου"),
        ]);
        foreach (var word in targets.Values.Append(source))
        {
            word.StrongNumber = "G5376";
            if (word.Text!.Slug.StartsWith("TR", StringComparison.Ordinal))
            {
                word.Text.Kind = TextKind.CriticalEdition;
                word.Text.Language = "grc";
            }
        }
        if (!await _db.Entities.AnyAsync(e => e.Slug == "philip-2"))
            _db.Entities.Add(new Entity { Slug = "philip-2", SourceId = "philip-2", Name = "Philip", Kind = EntityKind.Person, Source = "a test" });
        var tetrarch = new Entity { Slug = "philip-4", SourceId = "philip-4", Name = "Philip", Kind = EntityKind.Person, Source = "a test" };
        _db.Entities.Add(tetrarch);
        var tetrarchWord = targets.Single(p => p.Key.Reference == "LUK 3:1").Value;
        _db.WordEntities.Add(new WordEntity { Word = tetrarchWord, Entity = tetrarch, Method = LinkMethod.Manual, Source = "a test's tetrarch ruling" });
        foreach (var target in targets.Where(p => p.Key.Reference == "LUK 3:19").Select(p => p.Value))
        {
            var link = new Link { FromTextId = source.TextId, ToTextId = target.TextId,
                Relation = LinkRelation.Renders, Method = LinkMethod.StrongNumber, Confidence = 1,
                Provenance = new() { Source = "a fixture's stated number match" } };
            _db.LinkWords.Add(new LinkWord { Link = link, Word = source, Side = LinkSide.From });
            _db.LinkWords.Add(new LinkWord { Link = link, Word = target, Side = LinkSide.To });
        }
        await _db.SaveChangesAsync();
        await Load();
        var references = new OwnReferenceLoader(_db, NullLogger<OwnReferenceLoader>.Instance);
        await references.Load();
        await _db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        var wordIds = targets.Where(p => p.Key.Reference == "LUK 3:19").Select(p => p.Value.Id).Append(source.Id).ToArray();
        var named = await _db.WordEntities.Include(a => a.Entity).Where(a => wordIds.Contains(a.WordId)).ToListAsync();
        named.Should().HaveCount(3).And.OnlyContain(a => a.Entity!.Slug == "philip-2");
        named.Should().OnlyContain(a => a.Source.Contains("Textus Receptus"));
        named.Single(a => a.WordId == source.Id).Note.Should().Contain("Herodias");
        named.Where(a => a.WordId != source.Id).Should().OnlyContain(a => a.Note!.StartsWith("through "));
        (await _db.EntityVerses.AnyAsync(v => v.Entity!.Slug == "philip-2" && v.CanonicalBook == 42
            && v.CanonicalChapter == 3 && v.CanonicalVerse == 19 && v.Names)).Should().BeTrue();
        (await _db.WordEntities.SingleAsync(a => a.WordId == tetrarchWord.Id)).EntityId.Should().Be(tetrarch.Id);
        var annotationIds = named.Select(a => a.Id).Order().ToArray();
        await Load();
        var again = await references.Load();
        again.Written.Should().Be(0);
        again.Withdrawn.Should().Be(0);
        (await _db.WordEntities.Where(a => wordIds.Contains(a.WordId)).OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync())
            .Should().Equal(annotationIds);
    }

    /// <summary>
    /// Daughter is the word Jesus says to the woman with the issue of blood, and no name of hers: the
    /// ruling that gives her the three words heads her record by what the text says of her, says who
    /// decided, and leaves the slug, which her picture is filed under, as it was.
    /// </summary>
    [Fact]
    public async Task ADaughterAddressedByHerWordsIsTheWomanWithTheIssueOfBloodAndKeepsHerSlug()
    {
        await Load();

        var woman = await _db.Entities.Include(e => e.Claims).SingleAsync(e => e.Slug == "daughter");
        woman.Name.Should().Be("Woman with an issue of blood");
        woman.Distinguisher.Should().Contain("MAT 9:20-22");
        woman.Notes.Should().Contain("MAT 9:22; MRK 5:34; LUK 8:48").And.Contain("does not give her name");
        woman.Claims.Should().ContainSingle(claim => claim.Method == LinkMethod.Manual)
            .Which.Source.Should().Contain("2026-09-30");

        var words = SenseReadingFiles.AddressedRulings().Rulings.Select(r => _words[r.Word]);
        (await _db.WordEntities.Where(a => a.EntityId == woman.Id).Select(a => a.WordId).ToListAsync())
            .Should().BeEquivalentTo(words);
    }

    /// <summary>
    /// The woman's record was listed by a dataset and is headed, described and read by us, so the
    /// page credits the ruling and not the dataset, on a database that loaded the ruling before the
    /// credit moved as well as on a cold one. The dataset's listing stays as a claim.
    /// </summary>
    [Fact]
    public async Task ARecordTheRulingReHeadsIsCreditedToTheRulingAndACreditIsNotMovedTwice()
    {
        await Load();
        await Load();

        var woman = await _db.Entities.Include(e => e.Claims).SingleAsync(e => e.Slug == "daughter");
        woman.Source.Should().StartWith("Essenthos").And.Contain("2026-09-30");
        woman.Claims.Should().ContainSingle(claim => claim.Method == LinkMethod.StatedBySource)
            .Which.Source.Should().Be("a test");

        // A database that loaded the file before the credit moved: the dataset's credit is back and
        // the file is already recorded, so nothing else of the file runs again.
        woman.Source = "BibleData by a test";
        await _db.SaveChangesAsync();
        await Load();
        (await _db.Entities.SingleAsync(e => e.Slug == "daughter")).Source.Should().StartWith("Essenthos");
    }

    /// <summary>
    /// A record we wrote says it is ours, in the source a reader is shown and in a claim carrying
    /// the method and whoever decided. A dataset's row carries neither, so the two can never be
    /// confused.
    /// </summary>
    [Fact]
    public async Task ARecordWeWroteSaysItIsOursAndWhatEstablishedIt()
    {
        await Load();

        foreach (var ruling in _rulings.Where(r => r.Create is not null))
        {
            var written = await _db.Entities
                .Include(e => e.Claims)
                .Include(e => e.Verses)
                .SingleAsync(e => e.Slug == ruling.Create!.Slug);

            written.Source.Should().StartWith("Essenthos");
            written.SourceId.Should().StartWith("essenthos:");
            written.Claims.Should().ContainSingle()
                .Which.Should().Match<EntityClaim>(c =>
                    c.Method == LinkMethod.Manual && c.Confidence == null && c.Note == ruling.Why);
            written.Verses.Should().ContainSingle()
                .Which.Source.Should().StartWith("Essenthos");
        }
    }

    /// <summary>
    /// The verse a record rests on is read from the word rather than transcribed beside it, so the
    /// two cannot come to disagree.
    /// </summary>
    [Fact]
    public async Task TheVerseARecordRestsOnIsTheVerseOfTheWord()
    {
        await Load();

        var ruling = _rulings.First(r => r.Create is not null);
        var word = await _db.Words.SingleAsync(w => w.Id == _words[ruling.Word]);
        var reference = await _db.VerseReferences.SingleAsync(r => r.VerseId == word.VerseId && r.IsPrimary);
        var rests = await _db.EntityVerses.SingleAsync(v => v.Entity!.Slug == ruling.Create!.Slug);

        rests.CanonicalBook.Should().Be(reference.CanonicalBook);
        rests.CanonicalChapter.Should().Be(reference.CanonicalChapter);
        rests.CanonicalVerse.Should().Be(reference.CanonicalVerse);
    }

    /// <summary>
    /// The shape the owner asked for on Judges 4:11: the word names Hobab, and the record says out
    /// loud that he may be Reuel. A silence and a guess are both worse answers than a named doubt.
    ///
    /// An alternative names a record where the encyclopedia holds one and describes it where it does
    /// not — Jerioth may be Azubah, who has a page, and may equally be no name at all, which has
    /// none. What has to be true of every one of them is that a reader is told what it is.
    /// </summary>
    [Fact]
    public async Task AnUnsettledRecordNamesWhoElseItMightBe()
    {
        await Load();

        foreach (var ruling in _rulings.Where(r => r.Alternatives is { Count: > 0 }))
        {
            var slug = ruling.Create?.Slug ?? ruling.Existing!;
            var alternatives = await _db.EntityAlternatives
                .Include(a => a.Alternative)
                .Where(a => a.Entity!.Slug == slug)
                .ToListAsync();

            alternatives.Should().HaveCount(ruling.Alternatives!.Count);
            alternatives.Should().OnlyContain(a => a.Alternative != null || a.Describes != null);
            alternatives.Should().OnlyContain(a => a.Reason.Length > 0);
            alternatives.Should().OnlyContain(a => a.Source.StartsWith("Essenthos"));
        }
    }

    /// <summary>
    /// A ruling that names a record the encyclopedia already holds does not write a second one. The
    /// duplicate records this corpus already has to live with are exactly what that would produce.
    /// </summary>
    [Fact]
    public async Task ARulingOnAnExistingRecordCreatesNothing()
    {
        var outcome = await Load();

        outcome.Created.Should().Be(_rulings.Count(r => r.Create is not null));
        (await _db.Entities.CountAsync(e => e.Source.StartsWith("Essenthos")))
            .Should().Be(outcome.Created + _rulings.Count(r => r.Existing is not null && r.Says?.Name is not null));
    }

    /// <summary>
    /// A person decided who is named, so the seed carries no confidence — and a word reached across
    /// a link that is itself a guess does carry one, because there the reach is what is uncertain
    /// and not the decision.
    /// </summary>
    [Fact]
    public async Task ADecisionCarriedAcrossAnUncertainLinkPicksUpTheLinksConfidence()
    {
        var ruling = _rulings[0];
        var hebrew = await _db.Words.SingleAsync(w => w.Id == _words[ruling.Word]);
        var english = _db.WordAt(_english, 1, 1, 1);

        var link = new Link
        {
            FromTextId = hebrew.TextId,
            ToTextId = english.TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.5,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = hebrew, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = english, Side = LinkSide.To });
        await _db.SaveChangesAsync();

        await Load();

        var carried = await _db.WordEntities.SingleAsync(a => a.WordId == english.Id);
        carried.Method.Should().Be(LinkMethod.Manual);
        carried.Confidence.Should().Be(0.5);
    }

    /// <summary>
    /// The bulk pass is built and off. With no readings on disk there is nothing to count either
    /// way, and what matters is that the switch decides rather than the presence of the files.
    /// </summary>
    [Fact]
    public async Task TheBulkPassWritesNothingWhileTheSwitchIsOff()
    {
        var outcome = await Load();

        outcome.Withheld.Should().Be(0);
        (await _db.Entities.CountAsync(e => e.Source.StartsWith("Essenthos")))
            .Should().Be(outcome.Created + _rulings.Count(r => r.Existing is not null && r.Says?.Name is not null));
    }

    [Fact]
    public async Task RunningAgainWritesNothingFurther()
    {
        await Load();
        var entities = await _db.Entities.CountAsync();
        var annotations = await _db.WordEntities.CountAsync();
        var names = await _db.EntityNames.CountAsync();

        var again = await Load();

        again.AlreadyLoaded.Should().BeTrue();
        again.Labelled.Should().Be(0);
        (await _db.Entities.CountAsync()).Should().Be(entities);
        (await _db.WordEntities.CountAsync()).Should().Be(annotations);
        (await _db.EntityNames.CountAsync()).Should().Be(names);
    }

    /// <summary>
    /// A record with no <c>entity_name</c> row is reachable by its slug and by nothing else: it
    /// stands in no namesake group, the pass that declines a name into Ukrainian and Russian reads
    /// that table and cannot see it, and no resolution by Strong number arrives at it however
    /// plainly the word carries the number. So the record is named, with the number read off the
    /// word the ruling rests on rather than off the ruling beside it.
    /// </summary>
    [Fact]
    public async Task ARecordWeWroteCarriesTheNameItsNumberReachesItBy()
    {
        var outcome = await Load();

        outcome.Labelled.Should().Be(_rulings.Count(r => r.Create is not null));

        foreach (var ruling in _rulings.Where(r => r.Create is not null))
        {
            var written = await _db.Entities
                .Include(e => e.Names)
                .SingleAsync(e => e.Slug == ruling.Create!.Slug);

            var name = written.Names.Should().ContainSingle().Which;
            name.Label.Should().Be(ruling.Create!.Name);
            name.Kind.Should().Be("proper name");
            var greek = ruling.StrongNumber.StartsWith('G');
            name.HebrewStrongNumber.Should().Be(greek ? null : ruling.StrongNumber);
            name.GreekStrongNumber.Should().Be(greek ? ruling.StrongNumber : null);
        }
    }

    /// <summary>
    /// And a record written before this pass named anything is named on the next boot. The guard
    /// that skips the rulings is what would otherwise keep it unnamed for ever — the records this
    /// is about are already there, so a step that named only what it created on this boot would
    /// never reach one of them.
    /// </summary>
    [Fact]
    public async Task ARecordWrittenBeforeThisPassNamedAnythingIsNamedOnTheNextBoot()
    {
        await Load();
        _db.EntityNames.RemoveRange(await _db.EntityNames.ToListAsync());
        await _db.SaveChangesAsync();

        var again = await Load();

        again.AlreadyLoaded.Should().BeTrue();
        again.Labelled.Should().Be(_rulings.Count(r => r.Create is not null));
        (await _db.EntityNames.CountAsync()).Should().Be(again.Labelled);
    }

    /// <summary>
    /// A rulings file arriving on a corpus that already holds the others is applied on the next
    /// boot, and only it: the files are decided at different times, so whether one is recorded says
    /// nothing about another.
    /// </summary>
    [Fact]
    public async Task ARulingsFileTheCorpusDoesNotYetHoldIsAppliedAloneOnTheNextBoot()
    {
        await Load();
        var report = SenseReadingFiles.ReportRulings();
        await _db.WordEntities.Where(a => a.Source == report.Source).ExecuteDeleteAsync();
        var entities = await _db.Entities.CountAsync();

        var again = await Load();

        again.AlreadyLoaded.Should().BeFalse();
        again.Created.Should().Be(0);
        (await _db.Entities.CountAsync()).Should().Be(entities);
        (await _db.WordEntities.Where(a => a.Source == report.Source).Select(a => a.WordId).ToListAsync())
            .Should().BeEquivalentTo(report.Rulings.Select(r => _words[r.Word]));
    }

    /// <summary>
    /// 1 Chronicles 1:9, where a reading had put the trading place on the son of Cush. The ruling is
    /// the answer, so the reading at the word and the copy the links carried from it are taken back
    /// rather than left beside it.
    /// </summary>
    [Fact]
    public async Task ARulingTakesBackTheReadingItOverrulesAndItsCarriedCopy()
    {
        var ruling = SenseReadingFiles.GenealogyRulings().Rulings[0];
        var place = new Entity
        {
            Kind = EntityKind.Place, Slug = "raamah-2", Name = "Raamah", SourceId = "raamah-2", Source = "a test",
        };
        _db.Entities.Add(place);
        await _db.SaveChangesAsync();

        const string reading = "a reading of the verse by a-model, prompt sense-1, run to 2026-09-06";
        var rendering = _db.WordAt(_english, 1, 1, 1);
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _words[ruling.Word], EntityId = place.Id, Method = LinkMethod.ModelReading,
            Confidence = 0.99, Source = reading, Note = "read as the place",
        });
        _db.WordEntities.Add(new WordEntity
        {
            WordId = rendering.Id, EntityId = place.Id, Method = LinkMethod.ModelReading,
            Confidence = 0.97, Source = reading,
            Note = $"through BHSA word {_words[ruling.Word]}, linked by aligner",
        });
        await _db.SaveChangesAsync();

        await Load();

        (await _db.WordEntities.AnyAsync(a => a.EntityId == place.Id)).Should().BeFalse();
        (await _db.WordEntities.Include(a => a.Entity).SingleAsync(a => a.WordId == _words[ruling.Word]))
            .Entity!.Slug.Should().Be(ruling.Existing);
    }

    /// <summary>
    /// A word is ruled on once, unless a later ruling corrects the earlier one and says which answer
    /// it takes back. Two files ruling on one word otherwise both annotate it, and a reader would
    /// meet a word naming whichever of two decisions the loader happened to write first.
    /// </summary>
    [Fact]
    public void NoWordIsRuledOnTwiceUnlessTheLaterRulingCorrectsTheEarlier()
    {
        foreach (var word in _rulings.GroupBy(r => _words[r.Word]).Where(g => g.Count() > 1))
        {
            var (earlier, later) = (word.First(), word.Last());
            word.Count().Should().Be(2, $"word {word.Key} is ruled on once and corrected once at most");
            if (later.Alongside is not null)
            {
                later.Corrects.Should().BeNull();
                later.Alongside.Should().Be(earlier.Existing);
                var companionFiles = SenseReadingFiles.AllRulings().Where(f => f.Rulings.Contains(earlier) || f.Rulings.Contains(later));
                companionFiles.Should().OnlyContain(f => !f.Carry && f.Method == "manual");
            }
            else
                later.Corrects.Should().Be(earlier.Existing, $"the ruling on {later.Reference} corrects the earlier one");
        }
    }

    /// <summary>
    /// A name a dataset lists a verse for, which two readings of the verse gave to the same record:
    /// the word is that record's, under a source that names both models and the day, and says in
    /// who decided that no person read it.
    /// </summary>
    [Fact]
    public async Task ANameTwoReadingsGaveToOneRecordIsThatRecords()
    {
        var file = SenseReadingFiles.NamesakeSecondRulings();
        file.Rulings.Should().NotBeEmpty();
        file.Rulings.Should().OnlyContain(r => r.Existing != null && r.Create == null && r.Position > 0 && r.Surface.Length > 0
                                               && r.StrongNumber != null && r.StrongNumber.Length > 1 && r.Why.Length > 0);
        file.Source.Should().Contain("claude-sonnet").And.Contain("claude-opus").And.Contain("2026-09-30");
        file.DecidedBy.Should().Contain("No person read them");

        await Load();

        var slugs = await _db.Entities.ToDictionaryAsync(e => e.Slug, e => e.Id);
        var named = await _db.WordEntities.Where(a => a.Source == file.Source).ToListAsync();
        named.Select(a => (a.WordId, a.EntityId)).Should().BeEquivalentTo(
            file.Rulings.Select(r => (_words[r.Word], slugs[r.Existing!])));
    }

    /// <summary>
    /// The words the records a dataset supplied print and no word of ours named: each given to the
    /// record its reading names, under a source that says who read it and on whose instruction,
    /// and the record the text spells otherwise headed by the name it prints and credited to the ruling.
    /// </summary>
    [Fact]
    public async Task ARecordTheDatasetSuppliedIsGivenTheWordItsVersePrints()
    {
        var file = SenseReadingFiles.DatasetRecordRulings();
        file.Rulings.Should().NotBeEmpty();
        file.Rulings.Should().OnlyContain(r => r.Existing != null && r.Create == null && r.Position > 0 && r.Surface.Length > 0 && r.Why.Length > 0);
        file.Source.Should().Contain("claude-opus-5-5").And.Contain("2026-09-30").And.Contain("owner's instruction");
        file.DecidedBy.Should().Contain("No person read them");

        await Load();

        var slugs = await _db.Entities.ToDictionaryAsync(e => e.Slug, e => e.Id);
        var named = await _db.WordEntities.Where(a => a.Source == file.Source).ToListAsync();
        named.Select(a => (a.WordId, a.EntityId)).Should().BeEquivalentTo(
            file.Rulings.Select(r => (_words[r.Word], slugs[r.Existing!])));
        foreach (var ruling in file.Rulings.Where(r => r.Says?.Name is not null))
        {
            var record = await _db.Entities.AsNoTracking().SingleAsync(e => e.Slug == ruling.Existing);
            record.Name.Should().Be(ruling.Says!.Name);
            record.Source.Should().Be(file.Source);
        }
    }

    /// <summary>
    /// A correction takes back the answer it names, and the word names the record the correction
    /// gives it — on a corpus the earlier ruling was already written into as on a cold one.
    /// </summary>
    [Fact]
    public async Task ACorrectionReplacesTheRulingItCorrects()
    {
        await Load();

        foreach (var correction in _rulings.Where(r => r.Corrects is not null))
        {
            var named = await _db.WordEntities.Include(a => a.Entity)
                .Where(a => a.WordId == _words[correction.Word])
                .Select(a => a.Entity!.Slug)
                .ToListAsync();
            named.Should().Equal(correction.Create?.Slug ?? correction.Existing);
        }
    }

    /// <summary>
    /// Two captains of one name in one story, whose fathers the verses name differently: each is a
    /// record of his own, the word of Jeremiah 42:1 leaves the Maachathite's son, and both records say
    /// that whether they are one man is open.
    /// </summary>
    [Fact]
    public async Task TheSonOfHoshaiahIsAManOfHisOwnAndBothCaptainsSayTheQuestionIsOpen()
    {
        await Load();

        var file = SenseReadingFiles.AllRulings().Single(f => f.Rulings.Any(r => r.Existing == "jaazaniah-5"));
        var son = file.Rulings.Single(r => r.Existing == "jaazaniah-5");
        son.Reference.Should().Be("JER 42:1");
        son.Corrects.Should().Be("jaazaniah");
        (await _db.WordEntities.Where(a => a.WordId == _words[son.Word]).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("jaazaniah-5");

        var captains = await _db.Entities.Where(e => e.Slug == "jaazaniah" || e.Slug == "jaazaniah-5").ToListAsync();
        captains.Should().HaveCount(2).And.OnlyContain(e => e.Notes!.Contains("the text does not settle"));
        captains.Single(e => e.Slug == "jaazaniah").Distinguisher.Should().Contain("Maachathite").And.NotContain("Hoshaiah");
        captains.Single(e => e.Slug == "jaazaniah-5").Distinguisher.Should().Contain("son of Hoshaiah");
    }

    /// <summary>
    /// A record that held two men keeps the one its line and picture were made for, and the other is
    /// written as a record of his own from the word of his verse, taking it from the record that held
    /// him: Pashhur the priest of 1 Chronicles 9:12 leaves Zedekiah's envoy, the Merarite Amaziah
    /// leaves the priest of Bethel, and Levi the publican is a man of his own beside Matthew.
    /// </summary>
    [Theory]
    [InlineData("pashhur-the-priest", "pashhur", "1CH 9:12")]
    [InlineData("malchijah-father-of-pashhur", "malchijah-2", "1CH 9:12")]
    [InlineData("amaziah-the-merarite", "amaziah-3", "1CH 6:30")]
    [InlineData("elhanan-son-of-dodo", "elhanan", "2SA 23:24")]
    [InlineData("conaniah-chief-of-the-levites", "conaniah", "2CH 35:9")]
    [InlineData("benaiah-the-trumpeting-priest", "benaiah-4", "1CH 15:24")]
    public async Task AManARecordHeldBesideAnotherIsWrittenFromHisOwnVerse(string written, string held, string reference)
    {
        var ruling = _rulings.Single(r => r.Create?.Slug == written);
        ruling.Reference.Should().Be(reference);
        ruling.Corrects.Should().Be(held);
        var heldRecord = _db.Entities.SingleOrDefault(e => e.Slug == held)
                         ?? _db.Entities.Add(new Entity
                         {
                             Kind = EntityKind.Person, Slug = held, Name = held, SourceId = held, Source = "a test",
                         }).Entity;
        await _db.SaveChangesAsync();
        var heldId = heldRecord.Id;
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _words[ruling.Word], EntityId = heldId, Method = LinkMethod.ModelReading, Confidence = 0.8,
            Source = "a reading of the verse",
        });
        await _db.SaveChangesAsync();

        await Load();

        var record = await _db.Entities.SingleAsync(e => e.Slug == written);
        record.Source.Should().StartWith("Essenthos");
        record.Distinguisher.Should().NotBeNullOrEmpty();
        (await _db.WordEntities.Where(a => a.WordId == _words[ruling.Word]).Select(a => a.EntityId).ToListAsync())
            .Should().Equal(record.Id);
    }

    /// <summary>
    /// A ruling names its word by where it stands and how it reads, so a word that reads otherwise at
    /// that address is not the word ruled on: the load stops and says which, instead of putting the
    /// owner's decision on a stranger.
    /// </summary>
    [Fact]
    public async Task ARulingWhoseWordIsNotWhereItSaysStopsTheLoadAndNamesIt()
    {
        var ruling = _rulings[0];
        await _db.Words.Where(w => w.Id == _words[ruling.Word])
            .ExecuteUpdateAsync(set => set.SetProperty(w => w.Surface, "another"));

        var load = Load;

        (await load.Should().ThrowAsync<InvalidDataException>())
            .Which.Message.Should().Contain(ruling.Word.ToString()).And.Contain("never point it at whatever word");
        (await _db.WordEntities.AnyAsync()).Should().BeFalse();
    }

    /// <summary>
    /// A file is applied once and then skipped, so a word that lost its answer afterwards — a reload
    /// numbers a text's words afresh, and the annotations go with the old rows — is given it again on
    /// the next load, and on the load after that nothing is written.
    /// </summary>
    [Fact]
    public async Task AWordThatLostItsRulingsAnswerIsGivenItAgainOnce()
    {
        await Load();
        var file = SenseReadingFiles.NamesakeSecondRulings();
        var ruling = file.Rulings.First(r => _rulings.All(other => other.Corrects is null || other.Word != r.Word));
        await _db.WordEntities.Where(a => a.WordId == _words[ruling.Word]).ExecuteDeleteAsync();

        var again = await Load();

        again.Restored.Should().Be(1);
        (await _db.WordEntities.Include(a => a.Entity).SingleAsync(a => a.WordId == _words[ruling.Word]))
            .Should().Match<WordEntity>(a => a.Entity!.Slug == ruling.Existing && a.Source == file.Source);
        (await Load()).Restored.Should().Be(0);
    }
}
