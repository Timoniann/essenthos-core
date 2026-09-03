namespace Essenthos.Core.Endpoints;

/// <summary>
/// The records more than one endpoint group answers with. Everything else lives beside the
/// endpoint that returns it, and every one of them is registered in AppJsonSerializerContext.
///
/// These are the shapes DOC-0002 defined for two corpora. They are answered from the witness model
/// unchanged, so the client can move onto the new vocabulary a screen at a time rather than in one
/// jump — which is what DOC-0008 asks for. Where the old shape cannot say something the new model
/// knows, the field is named here and the mapping is explained where it is made.
/// </summary>
internal record BookRefResponse(int Ordinal, string Name, string Slug);

/// <param name="Books">
/// Every canonical book the text holds. <paramref name="FirstBook"/> and <paramref name="LastBook"/>
/// are kept because DOC-0002 defined them and clients read them, but they cannot be believed on
/// their own: the Septuagint's books are 1-39 and 67-81, so its span says it covers John.
/// </param>
internal record CoverageResponse(int FirstBook, int LastBook, IReadOnlyList<int> Books);

/// <param name="License">The licence's name, an SPDX identifier where one applies.</param>
/// <param name="RightsHolder">
/// Who to credit. CC BY and CC BY-NC both require the creator to be named and the licence to be
/// linked, so a page that prints only <c>CC-BY-NC-4.0</c> is not attribution — it is the name of
/// the obligation with the obligation unmet. The columns held all of this and nothing sent it.
/// </param>
internal record CorpusResponse(
    string Id,
    string Name,
    string Kind,
    string Language,
    string Direction,
    bool HasWordMapping,
    string? License,
    string? TextualBasis,
    string? Versification,
    int? PublicationYear,
    CoverageResponse Coverage)
{
    public string? RightsHolder { get; init; }

    public string? LicenseUrl { get; init; }

    /// <summary>
    /// How the licence requires this text to be cited, where a name and a URL cannot carry it.
    /// BHSA asks for its DOI in anything published from it, and that is an obligation rather than
    /// a courtesy.
    /// </summary>
    public string? Citation { get; init; }

    /// <summary>Where the text was obtained, so a reader can check what was loaded.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// Who put the text into the language it is in — a person where there is one, the body that
    /// made it where there is not. Null is silence: nobody known, or not a translation at all.
    /// </summary>
    public string? Translators { get; init; }

    /// <summary>Who established this edition, which is rarely whoever translated it.</summary>
    public string? Editors { get; init; }

    /// <summary>
    /// Which edition or revision this is, where the year alone does not identify it. Every digital
    /// King James is the modern standard text and not the 1611 printing, and a reader cannot tell
    /// from a publication year that says 1611.
    /// </summary>
    public string? Edition { get; init; }

    /// <summary>
    /// The year of the edition served, where that is not <paramref name="PublicationYear"/>. Null
    /// means they are the same year, not that nobody looked.
    /// </summary>
    public int? EditionYear { get; init; }

    /// <summary>What this text is and how it came to be, in a paragraph the columns cannot hold.</summary>
    public string? About { get; init; }

    /// <summary>
    /// What is unsettled or additional about the rights, beside the licence the source states. It
    /// belongs next to the licence rather than inside <see cref="About"/>: a contested claim of
    /// public domain is exactly what a reader deciding whether to republish must not miss.
    /// </summary>
    public string? RightsNote { get; init; }

    /// <summary>
    /// What the licence permits, in one word: <c>public-domain</c>, <c>attribution</c>,
    /// <c>non-commercial-only</c>, <c>share-alike</c>, <c>unknown</c>. Unknown is not permission.
    /// </summary>
    public string? Redistribution { get; init; }

    /// <summary>
    /// The other identifiers this text answers to, where other Bible software spells it
    /// differently: the Synodal is <c>syno</c> at YouVersion and <c>synod</c> at bolls.life as well
    /// as <c>rusv</c> here. Any of them may be sent in a path or in <c>?corpora=</c>, and
    /// <see cref="Id"/> is what comes back — a client that stores what it received keeps the
    /// canonical spelling. Null where a text has no other name, so a client can offer them without
    /// knowing which texts have any.
    /// </summary>
    public IReadOnlyList<string>? Aliases { get; init; }
}

/// <summary>
/// One nested object rather than twenty flat fields. Null means this text does not carry that
/// annotation, which is most of them for Greek and all of them for a translation.
/// </summary>
internal record MorphologyResponse(
    string? PartOfSpeech,
    string? Gender,
    string? Number,
    string? Person,
    string? State,
    string? Stem,
    string? Tense,
    string? LexicalSet,
    string? PhraseDependentPartOfSpeech,
    string? PronominalGender,
    string? PronominalNumber,
    string? PronominalPerson,
    string[]? Nametypes);

internal record EntityRefResponse(string Type, string Slug, string Name);

/// <param name="OriginalWordIds">
/// The words of other texts this word is linked to. Empty until the links are loaded, which is not
/// the same fact as a word that is linked to nothing — the difference is what
/// <c>hasWordMapping</c> on the corpus is for.
/// </param>
/// <param name="Phono">
/// How the word is pronounced, where the text carries it. BHSA does; nothing else so far.
/// </param>
internal record TextWordResponse(
    long Id,
    string Text,
    string Trailer,
    string? Gloss,
    string? Lexeme,
    string? StrongNo,
    long[] OriginalWordIds,
    string? MappingProvenance,
    /// <summary>
    /// Set where a source states that this word has no counterpart: <c>expands</c> where the
    /// translation supplies it and the original does not have it, <c>omits</c> for the reverse.
    /// Null is not the same fact — it means nothing was found, which is silence rather than a claim.
    /// </summary>
    string? Absence,
    EntityRefResponse? Entity,
    MorphologyResponse? Morphology,
    string? Phono,
    string? PhonoTrailer,
    string? Language)
{
    /// <summary>
    /// The source records this word and prints no letters for it: a Hebrew article that has
    /// assimilated into its preposition, a quotation mark that opens a verse. It carries annotation
    /// and it can be the far end of an alignment, so it is sent rather than dropped — but a
    /// renderer should not give it a span of its own, and counting words to reach a position should
    /// not count it.
    /// </summary>
    public bool Elided { get; init; }

    /// <summary>
    /// The edition prints this word as one it supplies: the translators put it there and the text
    /// they were translating has no counterpart for it. The Synodal says so with square brackets,
    /// 4,247 spans of them, and a renderer should show that — in brackets, in italics, however it
    /// shows an editorial hand — rather than as ordinary text.
    ///
    /// It is the edition's own statement about its own page, so it is not <see cref="Absence"/>,
    /// which is what an alignment against some other text concluded. A word can carry both, one,
    /// or neither.
    /// </summary>
    public bool Supplied { get; init; }
}

/// <param name="Label">
/// The letter this edition prints after the number, where it prints one — the Septuagint's Genesis
/// 31:50a. Empty for every other text, which number their verses and nothing else.
/// </param>
internal record TextVerseResponse(int Number, IList<TextWordResponse> Words, string Label = "");

internal record VerseRefResponse(int BookOrdinal, string Book, string Slug, int Chapter, int Verse);
