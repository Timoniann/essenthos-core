using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Endpoints;

/// <summary>The three counts of an entity's references, which are three different questions.</summary>
internal sealed record EntityTally(int References, int Mentions, int Disputed);

/// <param name="References">How many verses name this entity.</param>
/// <param name="Mentions">
/// How many times the datasets name it, which is the larger number: each lists a verse once, and a
/// verse two of them list is named twice.
/// </param>
internal record EntitySummaryResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    int References,
    int Mentions)
{
    /// <summary>
    /// What this corpus says the entity is, as the pieces of a line whose every name is a link, in
    /// the language it says it is in. Null where nothing has been generated for this entity yet.
    /// </summary>
    public EntityDescriptorResponse? Descriptor { get; init; }

    /// <summary>
    /// The name in the language asked for, where the corpus has one; null where it has none, or
    /// where the language asked for is the headword's own, and a client shows the English name.
    /// </summary>
    public string? LocalName { get; init; }

    /// <summary>
    /// The line under the name in the language asked for, where this corpus wrote the English line
    /// and has rendered it; null otherwise, and a client shows <see cref="Distinguisher"/> as the
    /// source's words.
    /// </summary>
    public string? LocalDistinguisher { get; init; }

    /// <summary>The picture the entity's page leads with, to show small; null where it has none.</summary>
    public EntityThumbnailResponse? Thumbnail { get; init; }

    /// <summary>What sort of object or observance it is; null on every other kind.</summary>
    public string? Subtype { get; init; }
}

internal record EntityListResponse(int Total, IList<EntitySummaryResponse> Items);

/// <summary>
/// A stretch of Scripture an object's or an observance's page sends a reader to, as the record lists
/// it: a role — <c>key</c> for one to read about it, <c>command</c> for one that appoints it — and the
/// span. A verse left null is the start or the end of its chapter.
/// </summary>
internal record PassageResponse(
    string Role,
    BookRefResponse Book,
    int Chapter,
    int? Verse,
    int EndChapter,
    int? EndVerse,
    string? Note);

/// <summary>
/// Where an observance falls, as the verse that appoints it says: the month counted from Abib, the
/// day of that month — or of the week, for a weekly one — and the last day where it runs longer.
/// Each is null where the text does not state it.
/// </summary>
internal record ObservanceTimeResponse(
    string Cycle,
    int? Month,
    int? Day,
    int? LastDay,
    VerseRefResponse Reference,
    string? Note);

/// <param name="Count">How many entities are filed under this letter; zero for an empty one.</param>
internal record EntityLetterResponse(string Letter, int Count);

/// <param name="Letters">
/// The language's alphabet in order — A to Z for English — every letter present, followed by any
/// other letter a name opens with.
/// </param>
internal record EntityLettersResponse(int Total, IList<EntityLetterResponse> Letters);

/// <summary>
/// How far one kind of entity's references actually reach, so that a count on a page can be read
/// as a fact about the dataset rather than as a fact about the text.
/// </summary>
/// <param name="Named">
/// How many of them any verse names. An entity the source lists and never cites is a row with no
/// references, which is a different fact from an entity the text never mentions.
/// </param>
/// <param name="Books">
/// The canonical books in which any reference of this kind falls, and nothing beyond them. Where
/// this is short of the canon, a page that says "1 verse" is reporting where the source stopped.
/// </param>
/// <param name="Sources">
/// Which datasets state this layer's references, and what each one alone reaches. The layer's own
/// totals are the union; these are the statements the union is made of.
/// </param>
internal record EntityLayerCoverageResponse(
    string Kind,
    int Entities,
    int Named,
    int References,
    int Mentions,
    CoverageResponse Books,
    IList<EntitySourceCoverageResponse> Sources);

/// <param name="Dataset">
/// Which of the declared datasets this is, where one claims the source string — the id a client
/// uses to reach its name, its licence and its credit. Null where nothing declares it, which is
/// how an undeclared source is noticed instead of being silently credited to nobody.
/// </param>
internal record EntitySourceCoverageResponse(
    string? Dataset,
    string Source,
    int References,
    int Mentions,
    CoverageResponse Books);

/// <param name="Canon">
/// How many books a complete layer would reach, so a client can say "2 of 66" without holding a
/// number of its own.
/// </param>
internal record EntityCoverageResponse(int Canon, IList<EntityLayerCoverageResponse> Layers);

/// <param name="References">
/// How many verses name this entity — the number a reader asking "how often is Nebuchadnezzar in
/// the text" is asking for.
/// </param>
/// <param name="Mentions">
/// How many namings those verses hold. The two differ by six per cent across the corpus and by a
/// third on Nebuchadnezzar, so they are both here and both labelled rather than one of them
/// standing in for the other.
/// </param>
/// <param name="Disputed">
/// Verses the source itself cannot resolve. BibleData holds the God of Israel and Jesus as one
/// entity; 1,417 New Testament namings use a word the New Testament gives both, and which of the
/// two is meant is a reading of the text rather than a fact about the dataset.
/// </param>
/// <param name="Claims">
/// What established this record, where anything beyond its dataset did. Empty for every record a
/// dataset supplied, whose <paramref name="Source"/> is the whole answer; one entry, with a method
/// and whoever decided, for a record this corpus wrote because a verse names somebody no dataset
/// holds.
/// </param>
/// <param name="Alternatives">
/// Who else this might be, where the evidence does not decide — and empty, which is the usual case,
/// where it does. A record with alternatives is not a weaker record: it is one that has said out
/// loud what a record without them is quietly assuming.
/// </param>
/// <param name="Unsettled">
/// Whether the identification is open. Derived from <paramref name="Alternatives"/> rather than
/// stored beside it, so the flag and the list cannot come apart.
/// </param>
internal record EntityResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    string? Sex,
    string? Tribe,
    string? PlaceKind,
    string? ModernEquivalent,
    string? Notes,
    string? OpenBibleId,
    EntityOriginResponse? Origin,
    string Source,
    string? SourceId,
    int References,
    int Mentions,
    int Disputed,
    IList<EntityNameResponse> Names,
    IList<EntityRelationshipResponse> Relationships,
    IList<EventResponse> Events,
    IList<EntityReferenceSourceResponse> ReferenceSources,
    IList<EntityClaimResponse> Claims,
    IList<EntityAlternativeResponse> Alternatives,
    bool Unsettled)
{
    /// <summary>
    /// This entity's own name in every case a pass produced for the language asked for.
    ///
    /// A relationship row is a sentence with two names in it and either end can be the one a case
    /// falls on: <em>Лот, син Гарана</em> puts the counterpart in the genitive, and the same row
    /// read from Haran's page puts Lot there. So the page needs its own forms as well as its
    /// counterparts', and neither can stand in for the other.
    /// </summary>
    public Dictionary<string, string>? Forms { get; init; }

    public EntityTribeResponse? TribeRecord { get; init; }

    /// <summary>
    /// What this corpus says the entity is: the pieces of a line, each name among them carrying the
    /// entity it names complete enough to be linked, and the claims the line was made of with the
    /// verse each was read from. <see cref="EntityDescriptorResponse.Language"/> says which language
    /// it came out in, which is not always the one asked for.
    ///
    /// <para>
    /// It is what <paramref name="Distinguisher"/> was being shown for, and it replaces it — but
    /// that field stays on the wire while the client is changed, because a field that vanishes
    /// mid-flight breaks whoever is reading it. Null where nothing has been generated for this
    /// entity.
    /// </para>
    /// </summary>
    public EntityDescriptorResponse? Descriptor { get; init; }

    /// <summary>
    /// Where the place is, as one point. Null on every person and people, and on a place its
    /// gazetteer cannot locate or locates only with coordinates this corpus does not hold.
    /// </summary>
    public EntityLocationResponse? Location { get; init; }

    /// <summary>The name in the language asked for, as on the index; null where there is none.</summary>
    public string? LocalName { get; init; }

    /// <summary>The line under the name in the language asked for, as on the index; null where there is none.</summary>
    public string? LocalDistinguisher { get; init; }

    /// <summary>
    /// How each text prints the name, counted from the words that name this entity there: one entry
    /// per text, its heading spelling first and every other spelling it prints after. Empty where no
    /// word of any text is named as this entity.
    /// </summary>
    public IList<EntityRenderingResponse> Renderings { get; init; } = [];

    /// <summary>
    /// Who the text gives this title to, each at the verse that names both. Empty on everything but
    /// a title, and on a title the text gives nobody by name.
    /// </summary>
    public IList<EntityTitleResponse> Bearers { get; init; } = [];

    /// <summary>The titles the text gives this person, each at the verse that names both.</summary>
    public IList<EntityTitleResponse> Titles { get; init; } = [];

    /// <summary>
    /// Its pictures, the one the page leads with first, each with who made it and under what licence.
    /// Empty where it has none — which is most people, and always God.
    /// </summary>
    public IList<EntityImageResponse> Images { get; init; } = [];

    /// <summary>What sort of object or observance it is — furnishing, structure, feast; null on every other kind.</summary>
    public string? Subtype { get; init; }

    /// <summary>The passages to read about it and those that command it, in the record's order.</summary>
    public IList<PassageResponse> Passages { get; init; } = [];

    /// <summary>Where an observance falls in the year, one entry per position a verse states.</summary>
    public IList<ObservanceTimeResponse> Times { get; init; } = [];
}

/// <param name="Corpus">The text, by the id every other response names it by.</param>
/// <param name="Language">The text's language, as the corpus codes it.</param>
/// <param name="Name">The spelling the text is headed with.</param>
/// <param name="Occurrences">How many words of the text name the entity, under every spelling.</param>
/// <param name="Spellings">Every spelling the text prints, the commonest first.</param>
internal record EntityRenderingResponse(
    string Corpus,
    string Language,
    string Name,
    int Occurrences,
    IList<EntitySpellingResponse> Spellings);

internal record EntitySpellingResponse(string Form, int Occurrences);

/// <summary>
/// The other end of a title held: the person on a title's page, the title on a person's.
/// </summary>
/// <param name="Reference">The verse that names the person and the title together.</param>
/// <param name="Note">What that verse says, so the claim can be checked against it.</param>
internal record EntityTitleResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    VerseRefResponse Reference,
    string? Note)
{
    /// <summary>Our own line under the counterpart's name in the language asked for, where this corpus rendered one.</summary>
    public string? LocalDistinguisher { get; init; }
}

/// <param name="Kind">
/// What the point stands for: <c>point</c> the place itself, <c>representative-point</c> a spot
/// inside a region or along a path, <c>center</c> the middle of the circle the place is somewhere
/// in, <c>settlement</c> the town somewhere inside which it stood.
/// </param>
/// <param name="Confidence">
/// The gazetteer's score for the identification, over a thousand: 0.5 and above is high confidence
/// and 1 very high. Not clamped, because the source's score is not — it runs past 1 and below 0.
/// </param>
internal record EntityLocationResponse(double Lon, double Lat, string Kind, double Confidence);

/// <param name="References">How many verses name the place, as on its page.</param>
/// <param name="Chapters">
/// Every chapter that names it, each as its book's canonical ordinal times a thousand plus the
/// chapter — 44013 is Acts 13 — in order, so a map can show the places of a book, a chapter or a
/// stretch of the story without asking again.
/// </param>
internal record PlacePointResponse(
    string Slug,
    string Name,
    double Lon,
    double Lat,
    string Kind,
    double Confidence,
    int References,
    IList<int> Chapters)
{
    /// <summary>
    /// What kind of place it is, as the entry states it: <c>settlement</c>, <c>mountain</c>,
    /// <c>river</c>, several comma-separated where the text calls it more than one. Null where
    /// the entry does not say.
    /// </summary>
    public string? PlaceKind { get; init; }

    /// <summary>The name in the language asked for, as on the index; null where there is none.</summary>
    public string? LocalName { get; init; }
}

/// <param name="Datasets">Whose points these are, as declared dataset ids, for the credit a map owes.</param>
internal record PlaceMapResponse(int Total, IList<string> Datasets, IList<PlacePointResponse> Items);

/// <param name="Confidence">
/// How sure, and null exactly where a person or a source stated it rather than a process concluding
/// it — the same rule the word annotations and the links follow.
/// </param>
internal record EntityClaimResponse(
    string Method,
    double? Confidence,
    string Source,
    string? Dataset,
    string? Note);

/// <summary>
/// Whom or where a people is named after, as a page a reader can open.
///
/// Null on every person and every place, and on the two thirds of peoples whose ancestor the
/// encyclopedia does not hold — where it is null the record's claim still says whom, in the words
/// of whoever said it, and only the link is missing.
/// </summary>
internal record EntityOriginResponse(string Slug, string Kind, string Name, string? Distinguisher);

internal record EntityTribeResponse(string Slug, string Kind, string Name, string? LocalName);

/// <param name="Slug">
/// The alternative's own page, where the encyclopedia holds one. Null where it does not, in which
/// case <paramref name="Describes"/> is all there is to say.
/// </param>
/// <param name="Dataset">
/// Who proposed it, as a declared dataset id, so a client can credit it the way it credits a claim
/// rather than print the row's own words. An alternative need not come from whoever supplied the
/// record it stands on.
/// </param>
internal record EntityAlternativeResponse(
    string? Slug,
    string? Name,
    string? Distinguisher,
    string? Describes,
    string Reason,
    string Source,
    string? Dataset)
{
    /// <summary>Our own line under the counterpart's name in the language asked for, where this corpus rendered one.</summary>
    public string? LocalDistinguisher { get; init; }
}

/// <summary>
/// Who states that the text names this entity, and how much of the count is theirs.
/// </summary>
/// <remarks>
/// <see cref="EntityResponse.Source"/> is who supplied the entity, which is a different question:
/// 110 of the places came from one dataset and are referenced almost entirely by another, so a
/// page crediting the reference list to the entity's source would name the wrong party on nearly
/// every place in the corpus.
/// </remarks>
internal record EntityReferenceSourceResponse(
    string? Dataset,
    string Source,
    int References,
    int Mentions);

/// <param name="HebrewStrongNumbers">
/// The lexicon entries this name is, one per word of it. A proper name has one; a title has as
/// many as it has words — <em>King of Judah</em> is H4428 and H3063 — which is why it is a list
/// and not a number.
/// </param>
internal record EntityNameResponse(
    string Label,
    string? Hebrew,
    string? HebrewTransliterated,
    string? Greek,
    string? GreekTransliterated,
    string? Meaning,
    IList<string> HebrewStrongNumbers,
    IList<string> GreekStrongNumbers,
    string? Kind);


/// <summary>What the text calls the entity at one verse, and whether that word settles who it is.</summary>
/// <param name="Dataset">
/// Which declared dataset states this naming. A place's references can come from two sources at
/// once, and the reader is owed which of them said the text names it here.
/// </param>
internal record EntityNamingResponse(string? Label, bool Disputed, string? Dataset);

/// <param name="Namings">
/// Every name the entity is given in this verse. Matthew 20:30 calls Jesus by name and by *Son of
/// David*, and both are here rather than the verse appearing twice.
/// </param>
/// <param name="Names">
/// True where a word of the verse is annotated to the entity, false where no word of it is.
/// </param>
/// <param name="Kind">
/// <c>named</c> where the verse names the entity; <c>spoken-of</c> where it was read as speaking of
/// the entity without the name, by a title, a description or a pronoun, and the naming's label is
/// the words that stand for it; <c>concerning</c> where a source lists the verse and no word of it
/// says who. The list gives them in that order.
/// </param>
internal record EntityReferenceResponse(
    BookRefResponse Book,
    int Chapter,
    int Verse,
    IList<EntityNamingResponse> Namings,
    bool Disputed,
    bool Names,
    string Kind);

internal record EntityReferenceListResponse(int Total, IList<EntityReferenceResponse> Items);
