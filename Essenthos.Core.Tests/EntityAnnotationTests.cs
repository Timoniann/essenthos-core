using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which words end up saying whom they name, and — the half that matters more — which words are
/// left saying nothing.
///
/// Every case here is one the data invites you to get wrong. A name twenty-three men share, a name
/// nobody in the encyclopedia bears, a city that appears in a man's title, a word BHSA marks as a
/// place whose only entity is a person, and a name BHSA declines to classify at all: each of them
/// would produce an annotation if the join were written the obvious way, and each of those
/// annotations would be a confident wrong answer a reader could not tell from scholarship.
///
/// <para>
/// They are asked of Postgres because the loader is a set of statements rather than a loop, and
/// what is under test is what those statements select.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityAnnotationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EntityAnnotationLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _english;

    /// <summary>
    /// Genesis 1, one verse per case, so a failure names the case rather than a position. The
    /// English is one word per verse standing opposite the Hebrew, plus a last word standing
    /// opposite two of them at once.
    /// </summary>
    public EntityAnnotationTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new EntityAnnotationLoader(_db, NullLogger<EntityAnnotationLoader>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["משה"]),
            (1, 2, ["זכריה"]),
            (1, 3, ["ירושלם"]),
            (1, 4, ["מלך"]),
            (1, 5, ["כנען"]),
            (1, 6, ["ישראל"]),
            (1, 7, ["פלמוני"]),
            (1, 11, ["כשׂדים"]),
            (1, 16, ["יפתח"]),
            (1, 17, ["יפתח"]),
            (1, 18, ["יריחו"]),
            (1, 19, ["משה", "משה"]));

        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["Moses"]),
            (1, 2, ["Zechariah"]),
            (1, 8, ["Both"]),
            (1, 9, ["Moses", "went"]),
            (1, 10, ["strode"]),
            (1, 12, ["When", "of", "Moses"]),
            (1, 13, ["Moses’s", "lifetime"]),
            (1, 14, ["Moses", "and", "Aaron"]),
            (1, 15, ["set", "out"]),
            (1, 19, ["Moses", "and", "Moses"]));

        _db.SaveChanges();

        Annotate(1, "H4872", "pers");
        Annotate(2, "H2148", "pers");
        Annotate(3, "H3389", "topo");
        Annotate(4, "H4428", "pers");
        Annotate(5, "H3667", "topo");
        Annotate(6, "H3478", "pers,gens,topo");
        Annotate(7, "H8888", "pers");
        Annotate(11, "H3778", "topo");
        Annotate(16, "H3316", "topo");
        Annotate(17, "H3316", "pers");
        Annotate(18, "H3405", "topo");
        Annotate(19, "H4872", "pers");
        Annotate(19, "H4872", "pers", 2);

        var moses = Person("moses", "Moses", "H4872");
        Person("zechariah-1", "Zechariah", "H2148");
        Person("zechariah-2", "Zechariah", "H2148");
        Place("jerusalem", "Jerusalem", "H3389");

        // A title is not a name. Its Strong numbers are the numbers of its words, so reading them
        // as names puts the city on the man who is called king of it.
        var adonizedek = Person("adonizedek", "Adonizedek", null);
        Name(adonizedek, "King of Jerusalem", "H4428,H3389", "title");

        // The land, whose only entity is the man it is named after. BHSA marks the word topo and
        // the encyclopedia answers with a person, and those are not the same claim.
        Person("canaan", "Canaan", "H3667");

        // The name BHSA itself declines to classify: person, people or place, on every occurrence.
        Person("jacob", "Israel", "H3478");

        // The land and the nation named for it, which are one Strong number and two records. A
        // people can never answer a word BHSA marks topo, so the candidate list still holds one
        // entity and the occurrence still resolves — but the marking is what made it resolve, and
        // that is a claim of a different kind from a number that named one thing to begin with.
        Place("chaldea", "Chaldea", "H3778");
        People("chaldeans", "Chaldeans", "H3778");

        // The judge and the town of Joshua 15:43, which Strong heads under one entry. A place is
        // not an aspect of a person, so nothing folds these into one record and the number is
        // borne by two — but BHSA says of each word which of the two it is.
        Person("jephthah", "Jephthah", "H3316");
        Place("jephthah-2", "Jephthah", "H3316");

        // And the two towns four kilometres apart, which the marking cannot tell apart because it
        // agrees with both.
        Place("jericho", "Jericho", "H3405");
        Place("jericho-2", "Jericho", "H3405");

        _db.SaveChanges();

        // The encyclopedia's own answer to the same question, for the one verse it speaks about.
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = moses.Id,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Source = "a test",
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private void Annotate(int verse, string number, string nameType, int position = 1)
    {
        var word = _db.WordAt(_hebrew, 1, verse, position);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse($$"""{"pos": "subs", "nameType": "{{nameType}}"}""");
        _db.SaveChanges();
    }

    private Entity Person(string slug, string name, string? number) =>
        Add(slug, name, EntityKind.Person, number);

    private Entity Place(string slug, string name, string? number) =>
        Add(slug, name, EntityKind.Place, number);

    private Entity People(string slug, string name, string? number) =>
        Add(slug, name, EntityKind.People, number);

    private Entity Add(string slug, string name, EntityKind kind, string? number)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = name, SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(entity);

        if (number is not null)
        {
            Name(entity, name, number, "name");
        }

        return entity;
    }

    private void Name(Entity entity, string label, string number, string kind) =>
        _db.EntityNames.Add(new EntityName
        {
            Entity = entity, Label = label, HebrewStrongNumber = number, Kind = kind,
        });

    /// <summary>
    /// A link the King James's own mapping states, which carries no confidence of its own, and one
    /// an aligner proposed, which does. What each is worth has to reach the annotation.
    /// </summary>
    private void Link(Word from, Word to, LinkMethod method, double? confidence)
    {
        var link = new Link
        {
            FromTextId = from.TextId,
            ToTextId = to.TextId,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = from, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = to, Side = LinkSide.To });
        _db.SaveChanges();
    }

    /// <summary>
    /// One link naming several words on each side, which is what a printed numbering writes where
    /// a verse uses one name more than once and the two texts write it a different number of times.
    /// </summary>
    private void Set(Word[] from, Word[] to)
    {
        var link = new Link
        {
            FromTextId = from[0].TextId,
            ToTextId = to[0].TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber,
            Confidence = 0.3,
            Source = "a test",
        };
        _db.Links.Add(link);

        foreach (var word in from)
        {
            _db.LinkWords.Add(new LinkWord { Link = link, Word = word, Side = LinkSide.From });
        }

        foreach (var word in to)
        {
            _db.LinkWords.Add(new LinkWord { Link = link, Word = word, Side = LinkSide.To });
        }

        _db.SaveChanges();
    }

    /// <summary>
    /// One link naming a phrase opposite a single original word, which is the shape a translation's
    /// own table states and an aligner never produces.
    /// </summary>
    private void Phrase(Word from, params Word[] to)
    {
        var link = new Link
        {
            FromTextId = from.TextId,
            ToTextId = to[0].TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = from, Side = LinkSide.From });

        foreach (var word in to)
        {
            _db.LinkWords.Add(new LinkWord { Link = link, Word = word, Side = LinkSide.To });
        }

        _db.SaveChanges();
    }

    private async Task<Dictionary<long, string>> Load()
    {
        await _loader.Load();
        return await _db.WordEntities
            .ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);
    }

    private Word Hebrew(int verse) => _db.WordAt(_hebrew, 1, verse, 1);

    [Fact]
    public async Task ANameOnlyOnePersonBearsIsAnnotatedWithThatPerson()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(1).Id).WhoseValue.Should().Be("moses");
    }

    /// <summary>
    /// The Zechariah case, which is the whole reason this loader stops where it does. The number
    /// is a name and the name is several men's, and nothing about the number says which.
    /// </summary>
    [Fact]
    public async Task ANameSeveralPeopleShareIsLeftUnannotated()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(2).Id);
    }

    [Fact]
    public async Task ANameNobodyInTheEncyclopediaBearsIsLeftUnannotated()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(7).Id);
    }

    [Fact]
    public async Task APlaceNameResolvesToThePlace()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(3).Id).WhoseValue.Should().Be("jerusalem");
    }

    /// <summary>
    /// Somebody else's annotation is not this loader's answer to whether it has run.
    ///
    /// The peoples are annotated onto the gentilic words a step earlier in the pipeline, so a pass
    /// that skipped because <em>something</em> was annotated would skip on every cold database and
    /// leave the corpus with no name resolutions in it at all — while logging that the words already
    /// say whom they name.
    /// </summary>
    [Fact]
    public async Task AnnotationsAnotherLoaderWroteDoNotStandInForThisOnes()
    {
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Hebrew(7).Id,
            EntityId = _db.Entities.Single(e => e.Slug == "moses").Id,
            Method = LinkMethod.Lexical,
            Confidence = 0.8,
            Source = "the gentilic Strong's Dictionary derives, resolving to exactly one people",
            Note = "somebody else's row",
        });
        await _db.SaveChangesAsync();

        var named = await Load();
        named.Should().ContainKey(Hebrew(1).Id).WhoseValue.Should().Be("moses");
    }

    /// <summary>
    /// Where the number named one record to begin with, the marking only agreed with it and there
    /// was nothing to choose. That is what <c>strong-number</c> asserts, and it is true here.
    /// </summary>
    [Fact]
    public async Task ANameNothingHadToChooseBetweenKeepsTheNumberAsItsMethod()
    {
        await _loader.Load();

        var moses = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(1).Id);
        moses.Method.Should().Be(LinkMethod.StrongNumber);
    }

    /// <summary>
    /// And where it did not. H3778 is Chaldea and the Chaldeans alike, so the number leaves the
    /// answer open; what settles it is BHSA analysing this occurrence as a toponym, which is a fact
    /// about the form of the word. Claiming <c>strong-number</c> there says no judgement was
    /// required, and a reader has no way to tell that from a name only one record ever bore.
    /// </summary>
    [Fact]
    public async Task ANameTheMarkingHadToChooseBetweenIsWrittenAsTheFormOfTheWord()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(11).Id).WhoseValue.Should().Be("chaldea");

        var chaldea = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(11).Id);
        chaldea.Method.Should().Be(LinkMethod.Lexical);
        chaldea.Confidence.Should().Be(0.9);
    }

    /// <summary>
    /// The claims go where the conclusion goes. An annotation whose method says the form decided it
    /// and whose only claim says the number did is a row that contradicts itself, and the claim is
    /// where an audit of provenance looks.
    /// </summary>
    [Fact]
    public async Task TheClaimOnSuchAnAnnotationSaysTheSame()
    {
        await _loader.Load();

        var chaldea = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(11).Id);
        var claims = await _db.WordEntityClaims
            .Where(c => c.WordEntityId == chaldea.Id)
            .ToListAsync();

        claims.Should().NotBeEmpty();
        claims.Should().OnlyContain(c => c.Method == LinkMethod.Lexical);
    }

    /// <summary>
    /// And it travels. A translated word is as good as the Hebrew word it renders, so it cannot
    /// claim a resolution the Hebrew word did not make.
    /// </summary>
    [Fact]
    public async Task TheFormTravelsToTheWordThatRendersIt()
    {
        var rendering = _db.WordAt(_english, 1, 10, 1);
        Link(Hebrew(11), rendering, LinkMethod.StatedBySource, null);

        await _loader.Load();

        var carried = await _db.WordEntities.SingleAsync(a => a.WordId == rendering.Id);
        carried.Method.Should().Be(LinkMethod.Lexical);
    }

    /// <summary>
    /// And a word can have a rival of its own that the word it was reached from does not.
    ///
    /// Nothing had to be chosen to read <em>משה</em> as Moses. But the reached word carries H3778,
    /// which Chaldea and the Chaldeans both bear, so of that word it is not true that no judgement
    /// was required — and the check asks the question of the word on the page, not of the one
    /// across the link. Where the editions differ this is the ordinary case rather than a corner:
    /// Scrivener prints a name one man bears where Nestle prints one that many do.
    /// </summary>
    [Fact]
    public async Task AReachedWordWithARivalOfItsOwnSaysSoToo()
    {
        var reached = _db.WordAt(_english, 1, 10, 1);
        reached.StrongNumber = "H3778";
        _db.SaveChanges();
        Link(Hebrew(1), reached, LinkMethod.StatedBySource, null);

        await _loader.Load();

        var carried = await _db.WordEntities.SingleAsync(a => a.WordId == reached.Id);
        carried.EntityId.Should().Be(_db.Entities.Single(e => e.Slug == "moses").Id);
        carried.Method.Should().Be(LinkMethod.Lexical);
    }

    /// <summary>
    /// <em>King of Jerusalem</em> records the numbers of both its words against Adonizedek. Read as
    /// a name, that makes H4428 — the common noun <em>king</em> — his name, and would annotate every
    /// king in the Hebrew Bible as him.
    /// </summary>
    [Fact]
    public async Task TheWordsOfATitleAreNotReadAsNames()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(4).Id);
    }

    /// <summary>
    /// The city keeps its own name even though a man's title contains it. Excluding the title has
    /// to exclude it from both directions of the count, or the city would be contested by the man.
    /// </summary>
    [Fact]
    public async Task ATitleDoesNotContestTheNameItContains()
    {
        var named = await Load();
        named.Should().ContainKey(Hebrew(3).Id).WhoseValue.Should().Be("jerusalem");
    }

    /// <summary>
    /// The land of Canaan, annotated as the person Canaan, is the mistake the old schema shipped.
    /// BHSA marks this occurrence a place and the encyclopedia holds only the man, so the two do
    /// not agree and nothing is written.
    /// </summary>
    [Fact]
    public async Task APlaceWhoseOnlyEntityIsAPersonIsLeftUnannotated()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(5).Id);
    }

    /// <summary>
    /// BHSA's name type belongs to the lemma, not to the occurrence: every occurrence of Israel is
    /// marked person, people and place at once, which says the name can be any of them and never
    /// that it is one here.
    /// </summary>
    [Fact]
    public async Task ANameBhsaWillNotClassifyIsLeftUnannotated()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(6).Id);
    }

    [Fact]
    public async Task AnAnnotationTravelsToTheWordThatRendersIt()
    {
        Link(Hebrew(1), _db.WordAt(_english, 1, 1, 1), LinkMethod.StatedBySource, null);

        var named = await Load();
        named.Should().ContainKey(_db.WordAt(_english, 1, 1, 1).Id).WhoseValue.Should().Be("moses");
    }

    /// <summary>
    /// A word can only be as sure as the step that reached it. A rendering an aligner proposed at
    /// 0.5 cannot carry an annotation as firmly as one the translators' own mapping states.
    /// </summary>
    [Fact]
    public async Task WhatTheLinkIsWorthReachesTheAnnotation()
    {
        var stated = _db.WordAt(_english, 1, 1, 1);
        Link(Hebrew(1), stated, LinkMethod.Aligner, 0.5);

        await _loader.Load();

        var carried = await _db.WordEntities.SingleAsync(a => a.WordId == stated.Id);
        var seed = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(1).Id);
        carried.Confidence.Should().BeApproximately(seed.Confidence!.Value * 0.5, 1e-9);
    }

    /// <summary>
    /// One English word standing opposite two Hebrew names is a word the corpus cannot resolve:
    /// the links say it renders Moses and they say it renders Jerusalem, and choosing between them
    /// is the judgement this loader refuses to make.
    /// </summary>
    [Fact]
    public async Task AWordReachedFromTwoNamesIsLeftUnannotated()
    {
        var both = _db.WordAt(_english, 1, 8, 1);
        Link(Hebrew(1), both, LinkMethod.StatedBySource, null);
        Link(Hebrew(3), both, LinkMethod.StatedBySource, null);

        var named = await Load();
        named.Should().NotContainKey(both.Id);
    }

    /// <summary>
    /// A word linked to an unresolved name stays unresolved. Carrying travels along the links from
    /// what the Hebrew resolved to, so a name nothing could settle reaches nobody.
    /// </summary>
    [Fact]
    public async Task AWordRenderingAnUnresolvedNameIsLeftUnannotated()
    {
        var zechariah = _db.WordAt(_english, 1, 2, 1);
        Link(Hebrew(2), zechariah, LinkMethod.StatedBySource, null);

        var named = await Load();
        named.Should().NotContainKey(zechariah.Id);
    }

    /// <summary>
    /// Every annotation names what asserted it. An annotation with no claim is one nobody can weigh
    /// afterwards, which is the failure the link claims were already caught by once.
    /// </summary>
    [Fact]
    public async Task EveryAnnotationCarriesAClaim()
    {
        await _loader.Load();

        var unclaimed = await _db.WordEntities
            .CountAsync(a => !_db.WordEntityClaims.Any(c => c.WordEntityId == a.Id));
        unclaimed.Should().Be(0);
    }

    /// <summary>
    /// The encyclopedia's list of verses is compiled from a reading of the text rather than from
    /// Strong numbers, so where it names the same entity in the same verse it is a second and
    /// independent answer — and two answers are worth recording as two.
    /// </summary>
    [Fact]
    public async Task AVerseTheEncyclopediaAgreesAboutCarriesASecondClaim()
    {
        await _loader.Load();

        var corroborated = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(1).Id);
        var alone = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(3).Id);

        var both = await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == corroborated.Id);
        var one = await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == alone.Id);

        both.Should().Be(2);
        one.Should().Be(1);
        corroborated.Confidence.Should().BeGreaterThan(alone.Confidence!.Value);
    }

    /// <summary>
    /// The start-up pipeline runs on every boot, so a second run has to be free and has to change
    /// nothing.
    /// </summary>
    [Fact]
    public async Task RunningAgainWritesNothingFurther()
    {
        await _loader.Load();
        var first = await _db.WordEntities.CountAsync();

        var again = await _loader.Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(first);
    }

    /// <summary>
    /// The verb the aligner had nowhere else to put. Moses is named in this verse and rendered
    /// firmly one word earlier, so the faint second reach from the same Hebrew word explains
    /// nothing and is the aligner's leftover.
    /// </summary>
    [Fact]
    public async Task AFaintLinkIsDroppedWhereTheVerseAlreadyRendersTheNameFirmly()
    {
        var rendering = _db.WordAt(_english, 1, 9, 1);
        var leftover = _db.WordAt(_english, 1, 9, 2);
        Link(Hebrew(1), rendering, LinkMethod.Aligner, 0.98);
        Link(Hebrew(1), leftover, LinkMethod.Aligner, 0.53);

        var named = await Load();

        named.Should().ContainKey(rendering.Id).WhoseValue.Should().Be("moses");
        named.Should().NotContainKey(leftover.Id);
    }

    /// <summary>
    /// The same faint link with nothing better beside it. Half the faint links in the corpus are
    /// true renderings an aligner scored badly, so a floor would take Nimrod along with the
    /// leftovers; the annotation stays, carrying what the link is worth.
    /// </summary>
    [Fact]
    public async Task AFaintLinkThatIsTheOnlyRenderingIsKept()
    {
        var only = _db.WordAt(_english, 1, 10, 1);
        Link(Hebrew(1), only, LinkMethod.Aligner, 0.53);

        var named = await Load();

        named.Should().ContainKey(only.Id).WhoseValue.Should().Be("moses");
        var carried = await _db.WordEntities.SingleAsync(a => a.WordId == only.Id);
        carried.Confidence.Should().BeLessThan(0.7);
    }

    /// <summary>
    /// A translation may put a word in a different verse from the one the Hebrew stands in, so a
    /// firm rendering elsewhere says nothing about this verse and cannot be the reason to empty it.
    /// </summary>
    [Fact]
    public async Task AFirmRenderingInAnotherVerseDoesNotDropAFaintOneHere()
    {
        var elsewhere = _db.WordAt(_english, 1, 9, 1);
        var only = _db.WordAt(_english, 1, 10, 1);
        Link(Hebrew(1), elsewhere, LinkMethod.Aligner, 0.98);
        Link(Hebrew(1), only, LinkMethod.Aligner, 0.53);

        var named = await Load();

        named.Should().ContainKey(only.Id).WhoseValue.Should().Be("moses");
    }

    /// <summary>
    /// Two words of one text can render one name between them — <em>of Abinoam</em> is two words in
    /// the King James — and both stand near each other in confidence. Dropping the weaker of any
    /// pair would take the second half of every such rendering, which is why only a faint link
    /// loses.
    /// </summary>
    [Fact]
    public async Task BothHalvesOfOneRenderingSurvive()
    {
        var first = _db.WordAt(_english, 1, 9, 1);
        var second = _db.WordAt(_english, 1, 9, 2);
        Link(Hebrew(1), first, LinkMethod.Aligner, 0.98);
        Link(Hebrew(1), second, LinkMethod.Aligner, 0.95);

        var named = await Load();

        named.Should().ContainKey(first.Id).WhoseValue.Should().Be("moses");
        named.Should().ContainKey(second.Id).WhoseValue.Should().Be("moses");
    }

    /// <summary>
    /// A translation's own table states that a phrase renders one original word, because that is
    /// what translation does: תֶּרַח in construct is <em>of Terah</em>, and the table has nowhere
    /// but the name to park the <em>When</em> that opens the clause. Giving the person to every
    /// word of the phrase is what puts the definite article on the page as a man.
    /// </summary>
    [Fact]
    public async Task OnlyTheHeadOfThePhraseALinkNamesIsAnnotated()
    {
        var opening = _db.WordAt(_english, 1, 12, 1);
        var supplied = _db.WordAt(_english, 1, 12, 2);
        var name = _db.WordAt(_english, 1, 12, 3);
        Phrase(Hebrew(1), opening, supplied, name);

        var named = await Load();

        named.Should().ContainKey(name.Id).WhoseValue.Should().Be("moses");
        named.Should().NotContainKey(opening.Id);
        named.Should().NotContainKey(supplied.Id);
    }

    /// <summary>
    /// The other shape of a set, and the one a head cannot serve. The Synodal's printed numbering
    /// pairs the three <em>Давид</em> of 1 Samuel 30:7 with the two דָּוִד beside them and cannot
    /// say which answers which, so it writes one link naming all five — and a head names one of the
    /// three and leaves the other two blank. Every word on the witness's side is one occurrence of
    /// the same lexeme, so the set holds nothing but the name, and each word of the set that is the
    /// name again renders it.
    ///
    /// <para>
    /// The <em>and</em> between them is the other half of the rule. The numbering sweeps a word
    /// into the set that is not the name at all — the <em>к</em> of Joshua 19:13, the
    /// <em>sister's</em> of Acts 23:16 — and naming every word of the set would put those on the
    /// page as people.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EveryWordOfASetThatIsTheNameAgainIsAnnotated()
    {
        var first = _db.WordAt(_english, 1, 19, 1);
        var joining = _db.WordAt(_english, 1, 19, 2);
        var second = _db.WordAt(_english, 1, 19, 3);
        Set(
            [_db.WordAt(_hebrew, 1, 19, 1), _db.WordAt(_hebrew, 1, 19, 2)],
            [first, joining, second]);

        var named = await Load();

        named.Should().ContainKey(first.Id).WhoseValue.Should().Be("moses");
        named.Should().ContainKey(second.Id).WhoseValue.Should().Be("moses");
        named.Should().NotContainKey(joining.Id, "the word between them is not the name");
    }

    /// <summary>
    /// And what it is worth is the link, which is what the set is worth. A numbering that cannot
    /// pair a repeated name is a weak claim about each of the words, and the row has to say so.
    /// </summary>
    [Fact]
    public async Task SuchAWordCarriesWhatTheContendedLinkIsWorth()
    {
        var first = _db.WordAt(_english, 1, 19, 1);
        Set(
            [_db.WordAt(_hebrew, 1, 19, 1), _db.WordAt(_hebrew, 1, 19, 2)],
            [first, _db.WordAt(_english, 1, 19, 2), _db.WordAt(_english, 1, 19, 3)]);

        await _loader.Load();

        var carried = await _db.WordEntities.SingleAsync(a => a.WordId == first.Id);
        var seed = await _db.WordEntities.SingleAsync(a => a.WordId == _db.WordAt(_hebrew, 1, 19, 1).Id);
        carried.Confidence.Should().BeApproximately(seed.Confidence!.Value * 0.3, 1e-9);
    }

    /// <summary>
    /// A set the witness's side does not fill with one name is a phrase, however many words stand
    /// opposite it — <em>the son of Terah</em> against <em>בֶּן תֶּרַח</em> — and the naming goes
    /// to the head as before. Here the second Hebrew word is another lexeme, so what the set says
    /// about the three words opposite is not that each of them is Moses.
    /// </summary>
    [Fact]
    public async Task ASetWhoseWitnessSideHoldsMoreThanOneLexemeKeepsTheHeadRule()
    {
        var first = _db.WordAt(_english, 1, 19, 1);
        var joining = _db.WordAt(_english, 1, 19, 2);
        var last = _db.WordAt(_english, 1, 19, 3);
        Set([Hebrew(1), Hebrew(7)], [first, joining, last]);

        var named = await Load();

        named.Should().ContainKey(last.Id).WhoseValue.Should().Be("moses");
        named.Should().NotContainKey(first.Id);
        named.Should().NotContainKey(joining.Id);
    }

    /// <summary>
    /// A set whose one number is two records on the witness's side names nothing. Joshua 19:47
    /// writes דן four times under one number, for the tribe, the town and the man their father, and
    /// opposite that, which word renders which is who is named: read as one name written several
    /// times, the set put the tribe on the Synodal's town and on the father, and its head is the
    /// father.
    /// </summary>
    [Fact]
    public async Task ASetWhoseWitnessSideNamesTwoEntitiesNamesNothing()
    {
        var first = _db.WordAt(_english, 1, 19, 1);
        var last = _db.WordAt(_english, 1, 19, 3);
        var other = Person("zipporah", "Zipporah", null);
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(_hebrew, 1, 19, 2).Id,
            Entity = other,
            Method = LinkMethod.Manual,
            Source = "a person",
        });
        _db.SaveChanges();
        Set(
            [_db.WordAt(_hebrew, 1, 19, 1), _db.WordAt(_hebrew, 1, 19, 2)],
            [first, _db.WordAt(_english, 1, 19, 2), last]);

        await _loader.Load();

        (await _db.WordEntities.AnyAsync(a => a.Word!.TextId == _english.Id && a.Word.Verse!.Number == 19))
            .Should().BeFalse("which of the two the head renders is as much a choice as any other word");
    }

    /// <summary>
    /// A possessive closes the name from the other end: the last word of <em>Terah's lifetime</em>
    /// is the thing possessed and the man is the word before it.
    /// </summary>
    [Fact]
    public async Task APossessiveEndsTheNameBeforeTheLastWordOfThePhrase()
    {
        var name = _db.WordAt(_english, 1, 13, 1);
        var possessed = _db.WordAt(_english, 1, 13, 2);
        Phrase(Hebrew(1), name, possessed);

        var named = await Load();

        named.Should().ContainKey(name.Id).WhoseValue.Should().Be("moses");
        named.Should().NotContainKey(possessed.Id);
    }

    /// <summary>
    /// Where the source grouped two names into one correspondence the head is the wrong one of
    /// them, and a man named as a different man reads as a fact where an article read as noise.
    /// The Berean puts <em>Tahrea and Ahaz</em> opposite <em>וְ תַחְרֵעַ</em>, so the last word
    /// says Ahaz is Tahrea and leaves Tahrea unmarked.
    /// </summary>
    [Fact]
    public async Task TheNameTheEncyclopediaRecordsTakesThePhraseFromItsLastWord()
    {
        var name = _db.WordAt(_english, 1, 14, 1);
        var joined = _db.WordAt(_english, 1, 14, 2);
        var other = _db.WordAt(_english, 1, 14, 3);
        Phrase(Hebrew(1), name, joined, other);

        var named = await Load();

        named.Should().ContainKey(name.Id).WhoseValue.Should().Be("moses");
        named.Should().NotContainKey(joined.Id);
        named.Should().NotContainKey(other.Id);
    }

    /// <summary>
    /// And it takes it only when it is the entity's own spelling. A phrase holding no name the
    /// encyclopedia records is ordered by position as before, because a word that merely shares a
    /// trigram or two with a name is not evidence of anything.
    /// </summary>
    [Fact]
    public async Task APhraseHoldingNoRecordedNameIsStillHeadedByItsLastWord()
    {
        var opening = _db.WordAt(_english, 1, 15, 1);
        var last = _db.WordAt(_english, 1, 15, 2);
        Phrase(Hebrew(1), opening, last);

        var named = await Load();

        named.Should().ContainKey(last.Id).WhoseValue.Should().Be("moses");
        named.Should().NotContainKey(opening.Id);
    }

    /// <summary>
    /// A leftover that names somebody else does not get to make the word unresolvable. The reach
    /// this loader refuses to weigh is a disagreement between two accounts it believes, and a faint
    /// link the verse has already outweighed is not one of those.
    /// </summary>
    [Fact]
    public async Task ADroppedLeftoverDoesNotVetoTheReadingItDisagreesWith()
    {
        var rendering = _db.WordAt(_english, 1, 9, 1);
        var contested = _db.WordAt(_english, 1, 9, 2);
        Link(Hebrew(3), rendering, LinkMethod.Aligner, 0.98);
        Link(Hebrew(3), contested, LinkMethod.Aligner, 0.53);
        Link(Hebrew(1), contested, LinkMethod.StatedBySource, null);

        var named = await Load();

        named.Should().ContainKey(contested.Id).WhoseValue.Should().Be("moses");
    }

    /// <summary>
    /// The counts the loader reports are the ones a reader is asked to trust, so they are counted
    /// rather than estimated, and counted at the grain the loader actually decides at: a number
    /// under the kind its words are marked. Two names are several bearers' of the marked kind —
    /// the Zechariahs and the two Jerichos — and three answer with nobody of it: the common noun
    /// <em>king</em>, the name no record bears, and the land of Canaan, whose only record is the
    /// man. Counted per number alone the last of those would read as an answer, and the loader
    /// writes nothing for it.
    /// </summary>
    [Fact]
    public async Task TheOutcomeCountsWhatItRefusedAsWellAsWhatItWrote()
    {
        var outcome = await _loader.Load();

        outcome.Hebrew.Contested.Should().Be(2);
        outcome.Hebrew.Unanswered.Should().Be(3);
        outcome.ByText.Should().ContainSingle().Which.Text.Should().Be(EntityCandidates.Witness);
    }

    /// <summary>
    /// The judge and the town named after him. Strong heads one entry for both, so the number is
    /// borne by two records and answers neither on its own — and BHSA marks this occurrence a
    /// place, of which there is exactly one. Asked of the number before the kind, both words name
    /// nobody; asked of the number and the kind together, each names the record it is.
    /// </summary>
    [Fact]
    public async Task ANameAManAndTheTownNamedAfterHimShareResolvesByTheMarking()
    {
        var named = await Load();

        named.Should().ContainKey(Hebrew(16).Id).WhoseValue.Should().Be("jephthah-2");
        named.Should().ContainKey(Hebrew(17).Id).WhoseValue.Should().Be("jephthah");
    }

    /// <summary>
    /// And it says so. The number is several records' and the marking is what chose between them,
    /// which is a claim of a different kind from a number that named one thing to begin with.
    /// </summary>
    [Fact]
    public async Task SuchAnOccurrenceIsWrittenAsTheFormOfTheWord()
    {
        await _loader.Load();

        var town = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(16).Id);
        town.Method.Should().Be(LinkMethod.Lexical);
    }

    /// <summary>
    /// What the marking cannot do is choose between two records it agrees with. Jericho at Tell es
    /// Sultan and Jericho at Tell el Alayiq are both places, so a word marked a place still has two
    /// answers and the corpus goes on saying nothing — which is the whole point of asking the
    /// resolution per kind rather than collapsing the kinds together.
    /// </summary>
    [Fact]
    public async Task ANameTwoPlacesBearIsStillLeftUnannotated()
    {
        var named = await Load();
        named.Should().NotContainKey(Hebrew(18).Id);
    }

    /// <summary>
    /// A record added after this pass has already run gets its words on the next boot.
    ///
    /// It is the ordinary case rather than a corner: the peoples, the records this corpus writes
    /// for itself and the place register all write entities, and every one of them runs after this.
    /// A pass that asked only whether it had ever run would answer yes here and leave the new
    /// record with no words at all.
    /// </summary>
    [Fact]
    public async Task ARecordAddedAfterThePassHasRunIsAnnotatedOnTheNextBoot()
    {
        var before = await Load();
        before.Should().NotContainKey(Hebrew(7).Id);

        Person("palmoni", "Palmoni", "H8888");
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        outcome.AlreadyLoaded.Should().BeFalse();
        outcome.Written.Should().Be(1);

        var after = await _db.WordEntities.ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);
        after.Should().ContainKey(Hebrew(7).Id).WhoseValue.Should().Be("palmoni");
        after.Where(a => a.Key != Hebrew(7).Id).Should().BeEquivalentTo(before);
    }

    /// <summary>
    /// A thing that comes to bear a man's name — the pillar Boaz beside Ruth's husband — turns the
    /// man's resolutions into choices the marking made. On the next boot the man's words are his by
    /// the form of the word, which a reading of the verse outranks, and no longer by a number only
    /// he bore.
    /// </summary>
    [Fact]
    public async Task ANameAThingComesToBearIsTheMansByTheFormOfTheWord()
    {
        Person("palmoni", "Palmoni", "H8888");
        await _db.SaveChangesAsync();
        await _loader.Load();
        (await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(7).Id)).Method.Should().Be(LinkMethod.StrongNumber);

        Add("palmoni-pillar", "Palmoni", EntityKind.Object, "H8888");
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        outcome.Withdrawn.Should().Be(1);
        var named = await _db.WordEntities.Include(a => a.Entity).SingleAsync(a => a.WordId == Hebrew(7).Id);
        named.Entity!.Slug.Should().Be("palmoni");
        named.Method.Should().Be(LinkMethod.Lexical);
    }

    /// <summary>
    /// And its name still travels. The carrying step reads what this run seeded, so a record
    /// reached on a later boot has to reach the words that render it on that boot too — otherwise
    /// the new record is named in the Hebrew and nowhere a reader of a translation would see it.
    /// </summary>
    [Fact]
    public async Task ARecordAnnotatedOnALaterBootStillReachesTheWordThatRendersIt()
    {
        var rendering = _db.WordAt(_english, 1, 10, 1);
        Link(Hebrew(7), rendering, LinkMethod.StatedBySource, null);

        await _loader.Load();

        Person("palmoni", "Palmoni", "H8888");
        await _db.SaveChangesAsync();

        await _loader.Load();

        var named = await _db.WordEntities.ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);
        named.Should().ContainKey(rendering.Id).WhoseValue.Should().Be("palmoni");
    }

    /// <summary>
    /// A record this pass has already spoken for is not asked again, and nothing else moves with
    /// it. What is under test is the second half of that: the run that annotates the newcomer
    /// leaves every row it wrote before exactly as it was.
    /// </summary>
    [Fact]
    public async Task AnnotatingANewcomerLeavesTheRowsAlreadyWrittenAlone()
    {
        Link(Hebrew(1), _db.WordAt(_english, 1, 1, 1), LinkMethod.StatedBySource, null);
        await _loader.Load();

        var before = await _db.WordEntities
            .Select(a => new { a.Id, a.WordId, a.EntityId, a.Method, a.Confidence, a.Source })
            .OrderBy(a => a.Id)
            .ToListAsync();
        var written = before.Select(a => a.Id).ToList();

        Person("palmoni", "Palmoni", "H8888");
        await _db.SaveChangesAsync();
        await _loader.Load();

        var after = await _db.WordEntities
            .Where(a => written.Contains(a.Id))
            .Select(a => new { a.Id, a.WordId, a.EntityId, a.Method, a.Confidence, a.Source })
            .OrderBy(a => a.Id)
            .ToListAsync();

        after.Should().BeEquivalentTo(before);
    }
}
