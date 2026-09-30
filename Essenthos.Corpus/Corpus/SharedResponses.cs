namespace Essenthos.Core.Corpus;

/// <summary>
/// The records more than one endpoint group answers with. Everything else lives beside the
/// endpoint that returns it, and every one of them is registered in AppJsonSerializerContext.
///
/// These are the shapes the v1 contract defined for two corpora. They are answered from the witness
/// model unchanged, so the client can move onto the new vocabulary a screen at a time rather than in
/// one jump. Where the old shape cannot say something the new model knows, the field is named here
/// and the mapping is explained where it is made.
/// </summary>
internal record BookRefResponse(int Ordinal, string Name, string Slug);

/// <param name="Books">
/// Every canonical book the text holds. <paramref name="FirstBook"/> and <paramref name="LastBook"/>
/// are kept because the v1 contract defined them and clients read them, but they cannot be believed
/// on their own: the Septuagint's books are 1-39 and 67-81, so its span says it covers John.
/// </param>
internal record CoverageResponse(int FirstBook, int LastBook, IReadOnlyList<int> Books);

/// <param name="License">The licence's name, an SPDX identifier where one applies.</param>
/// <param name="RightsHolder">
/// Who to credit. CC BY and CC BY-NC both require the creator to be named and the licence to be
/// linked, so a page that prints only <c>CC-BY-NC-4.0</c> is not attribution — it is the name of
/// the obligation with the obligation unmet. The columns held all of this and nothing sent it.
/// </param>
/// <param name="Covers">Which words of the text are this source's.</param>
internal record PartSourceResponse(string Name, string Author, string Licence, string? LicenceUrl, string Url, string Covers);

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
    /// differently: the Synodal is <c>SYNO</c> at YouVersion as well
    /// as <c>RUSV</c> here. Any of them may be sent in a path or in <c>?corpora=</c>, and
    /// <see cref="Id"/> is what comes back — a client that stores what it received keeps the
    /// canonical spelling. Null where a text has no other name, so a client can offer them without
    /// knowing which texts have any.
    /// </summary>
    public IReadOnlyList<string>? Aliases { get; init; }

    /// <summary>
    /// What kind of text this is, finer than <c>Kind</c>: <c>manuscript-tradition</c>,
    /// <c>critical-edition</c>, <c>printed-edition</c> or <c>translation</c>. <c>Kind</c> keeps the
    /// old contract's two answers; this one says whether a Greek text was weighed from manuscripts
    /// or reprinted from somebody's edition.
    /// </summary>
    public string? Form { get; init; }

    /// <summary>
    /// The ISO 15924 script the text is printed in, where <see cref="Language"/> does not settle it:
    /// <c>Hant</c> or <c>Hans</c> for Chinese. A client joins the two into a language tag
    /// (<c>zh-Hant</c>), which is what picks a traditional or a simplified typeface. Null otherwise.
    /// </summary>
    public string? Script { get; init; }

    /// <summary>
    /// The sources some of its words come from beside <see cref="SourceUrl"/> — a verse a
    /// digitisation lost, restored from another copy — each with its own author, terms and address,
    /// and which words are its. Null where every word comes from the one source.
    /// </summary>
    public IReadOnlyList<PartSourceResponse>? PartSources { get; init; }

    /// <summary>
    /// What the text is, in a sentence or two, keyed by interface language — <c>en</c>, <c>uk</c>,
    /// <c>de</c>, <c>es</c>. It restates <see cref="About"/>, which stays the record. Sent by the
    /// text listings and nowhere else; null for a text nobody has described.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Summary { get; init; }

    /// <summary>
    /// What the text is in one short sentence, keyed as <see cref="Summary"/> is: the line a list of
    /// texts shows beside the facts it compares. Sent by the list of texts only; null for a text
    /// nobody has described.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tagline { get; init; }
}

/// <summary>
/// One nested object rather than twenty flat fields. Null means this text does not carry that
/// annotation, which is most of them for Greek and all of them for a translation.
///
/// <para>
/// The field set came from a Hebrew text and shows it: state and stem are BHSA's, and until now
/// there was nowhere at all to put a Greek word's case, which for a reader of the Greek is the one
/// thing they most want the word to say. It is stored, and was simply never read out.
/// </para>
/// </summary>
internal record MorphologyResponse(
    string? PartOfSpeech,
    string? Case,
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

/// <summary>
/// Who a word names — <c>person</c> or <c>place</c>, and which one — for the card a reader gets by
/// hovering it.
/// </summary>
internal record EntityRefResponse(string Type, string Slug, string Name)
{
    /// <summary>
    /// What established this, as the link provenance spells it: <c>strong-number</c> where the name
    /// resolved through the lexicon and nothing had to be chosen, <c>lexical</c> where the number
    /// is several records' and the form of the word chose between them, <c>manual</c> where a
    /// person said so. Null only for an older annotation that predates the field.
    ///
    /// It is here because the alternative was a card that says <em>Moses</em> in the same voice
    /// whether a source stated it or something guessed, and a reader cannot ask afterwards.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// How sure, between 0 and 1, and null exactly where a source or a person stated it rather than
    /// a process concluding it. Null is therefore the strongest value and not the weakest.
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// Who or what said so, in the words the row carries: the file, the reasoning and its version,
    /// or the person. <see cref="Method"/> says what kind of claim it is and this says whose it is,
    /// and the two together are what lets a card built from a model's reading of the passage be
    /// told from one a lexicon resolved — which is the whole difference between an encyclopedia and
    /// a plausible-looking guess.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// The route or the reason, where the row carries one: the number the resolution went through,
    /// the word it was carried from, or the sentence a reading gave for its answer.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// The name this word names the record under, where it is not the one the record heads with:
    /// <em>Abraham</em> over a record headed <em>Abram</em>, <em>Paul</em> over <em>Saul</em>. Read
    /// from the Strong number the naming went through, which the record's names each carry. Null
    /// where the word bears the heading's name or the number does not settle which.
    /// </summary>
    public string? NamedAs { get; init; }
}

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
    /// they were translating has no counterpart for it — the King James' italics, the Synodal's and
    /// the Berean's brackets — and a renderer should show that however it shows an editorial hand,
    /// rather than as ordinary text. The same as <see cref="Mark"/> being <c>supplied</c>.
    ///
    /// It is the edition's own statement about its own page, so it is not <see cref="Absence"/>,
    /// which is what an alignment against some other text concluded. A word can carry both, one,
    /// or neither.
    /// </summary>
    public bool Supplied { get; init; }

    /// <summary>
    /// What the edition itself prints about this word, where it prints anything: <c>supplied</c>,
    /// <c>doubtful</c> for words its editors doubt belong to the text, <c>subscription</c> for a
    /// scribe's note that is not the text, <c>restored</c> for words taken from other manuscripts.
    /// Null for a word the edition prints plainly, which is nearly all of them.
    /// </summary>
    public string? Mark { get; init; }

    /// <summary>
    /// A Strong number the corpus worked out for this word, where the source states none and the
    /// working out arrived at one number only. Null where the source states a number — that is
    /// <see cref="StrongNo"/> — and where the corpus proposed several or none.
    ///
    /// It is a separate field because it is a separate claim. <see cref="StrongNo"/> is always a
    /// source's; this is ours, and it says how it was reached and how sure, so a client can offer
    /// the dictionary entry and still say it came by the dictionary form rather than from the
    /// edition.
    /// </summary>
    public StrongCandidateResponse? StrongCandidate { get; init; }

    /// <summary>
    /// A lexicon's short gloss for a Greek word its edition does not gloss, and how the word reached
    /// it. Null where the edition glosses the word itself — that is <see cref="Gloss"/> — and where
    /// no way reached an entry.
    /// </summary>
    public LexiconGlossResponse? LexiconGloss { get; init; }

    /// <summary>
    /// For a word of a text no lexicon of its own language glosses, what the Greek word it is aligned
    /// to means — which is a statement about the Greek and the alignment, never about this word, and
    /// says so by carrying the Greek word and the link's method and confidence with it.
    /// </summary>
    public ThroughGreekResponse? ThroughGreek { get; init; }

    /// <summary>The entry of the Ge'ez lexicon this word is a form of, where that could be said without choosing.</summary>
    public GeezEntryResponse? GeezEntry { get; init; }

    /// <summary>
    /// Every record the word names, in a fixed order, <see cref="Entity"/> first: <em>Christ</em> names
    /// the title and the man who bears it, <em>the Shunammite</em> Abishag and her people. Empty for a
    /// word that names nothing. <see cref="Entity"/> stays for a client that reads one.
    /// </summary>
    public IReadOnlyList<EntityRefResponse> Entities { get; init; } = [];

    /// <summary>
    /// The edition starts a new paragraph (<c>paragraph</c>) or a new line (<c>line</c>) before this
    /// word. Null where it marks nothing, which for most texts is everywhere: silence, not a claim
    /// that the text runs on.
    /// </summary>
    public string? Break { get; init; }
}

/// <param name="Glosses">
/// Every gloss the lexicon gives the entries the word reached, in the lexicon's order. More than one
/// where a form or a number is filed under several entries; which of them this word is was not
/// decided here.
/// </param>
/// <param name="Via">
/// How the word reached the entry: <c>strong</c> through the number its edition prints, <c>lemma</c>
/// through its own dictionary form, <c>equals</c> through the dictionary form of the word it is
/// linked to as the same word in another edition, <c>strong-candidate</c> through the number the
/// corpus proposed from its form. In that order the claim grows weaker, and a reader is told which.
/// </param>
internal record LexiconGlossResponse(string[] Glosses, string Via);

/// <param name="Corpus">The Greek text the aligned word is in.</param>
/// <param name="Word">The Greek word as that text prints it.</param>
/// <param name="Glosses">What it means, as its edition glosses it or as the Greek lexicon does.</param>
/// <param name="Method">What made the link, spelled as a link's method is: <c>aligner</c>.</param>
internal record ThroughGreekResponse(
    string Corpus,
    string Word,
    string? Lemma,
    string[] Glosses,
    string Method,
    double? Confidence);

/// <param name="Latin">Dillmann's first Latin glosses, in his order and untranslated.</param>
/// <param name="Greek">The Greek Dillmann gives for it, the short equivalents first.</param>
/// <param name="Via">
/// <c>form</c> where the word is spelt as this headword and no other once what is written onto it
/// is taken off; <c>greek</c> where several headwords could be meant and the Greek it is aligned to
/// is Dillmann's Greek for this one.
/// </param>
internal record GeezEntryResponse(string Headword, string[] Latin, string[] Greek, string Via);

/// <param name="Method">What produced the number, spelled as a link's method is: <c>lexical</c>.</param>
/// <param name="Confidence">How sure, between 0 and 1; null only where a source stated it.</param>
/// <remarks>
/// Whose working it is stays in the database: it is one long sentence, the same on every word of a
/// chapter, and sent per word it would be a fifth of the parallel response.
/// </remarks>
internal record StrongCandidateResponse(string Number, string Method, double? Confidence);

/// <param name="Label">
/// The letter this edition prints after the number, where it prints one — the Septuagint's Genesis
/// 31:50a. Empty for every other text, which number their verses and nothing else.
/// </param>
/// <summary>A footnote or a cross-reference the edition prints beside a verse.</summary>
/// <param name="AnchorWordId">
/// The word this edition printed the note marker after, as an id of <see cref="TextWordResponse"/>
/// in the same verse — null for a verse-level note. It is the id and not the spelling because a
/// verse routinely repeats a word: 228 of the corpus's 1,755 anchored notes sit on a spelling that
/// occurs more than once in their own verse, and a renderer matching on letters would put those
/// markers in the wrong place with no way to know it had.
///
/// It says where the marker was printed. It does not assert that the note explains only that word.
/// </param>
/// <param name="Anchor">
/// That word's spelling, so a reader can be told which word the marker follows without the client
/// having to look it up. Null exactly when <see cref="AnchorWordId"/> is.
/// </param>
internal record SourceNoteResponse(string Kind, string Content, long? AnchorWordId = null, string? Anchor = null);

internal record TextVerseResponse(int Number, IList<TextWordResponse> Words, string Label = "")
{
    /// <summary>
    /// Notes are the source edition speaking about this verse, not an application commentary and
    /// never a part of <see cref="Words"/>.
    /// </summary>
    public IList<SourceNoteResponse> Notes { get; init; } = [];
}

internal record VerseRefResponse(int BookOrdinal, string Book, string Slug, int Chapter, int Verse);

/// <param name="Category">
/// <c>read</c>: read here from the verse on the row, by whatever <see cref="Source"/> names.
/// </param>
/// <param name="Inward">
/// True when this is the other entity's relationship read backwards — Isaac is recorded as the son
/// of Abraham, and Abraham's page shows the same row from his side.
/// </param>
internal record EntityRelationshipResponse(
    string Type,
    string Category,
    string Slug,
    string Name,
    string? Distinguisher,
    bool Inward,
    VerseRefResponse? Reference,
    string? Notes)
{
    /// <summary>
    /// The counterpart's name in every case a pass produced for the language asked for, keyed by
    /// case. Null where no pass has declined this name into that language, which is where the
    /// English name is the whole of what a renderer can say.
    ///
    /// A row is a sentence — <em>Лот, син Гарана</em> — and the case is not decoration: <em>син
    /// Гарана</em> and <em>син Гаран</em> differ in whether the sentence is Ukrainian. Nothing
    /// computes it, because a stemmer guessing the genitive of a Hebrew proper name is wrong often
    /// and silently, which is why this is a form the corpus holds rather than a rule it applies.
    /// </summary>
    public Dictionary<string, string>? Forms { get; init; }

    /// <summary>
    /// What the counterpart is — <c>person</c>, <c>place</c>, <c>people</c> — so a page links it to
    /// the right address. The rows are not only between people: YHVH is the creator of heaven, which
    /// is a place, and David the king of the Israelites, who are a people.
    /// </summary>
    public string Kind { get; init; } = "person";

    /// <summary>
    /// What established it: a model's reading of the verse, or a person's decision. A reader who
    /// cannot tell the two apart is being asked to trust both equally.
    /// </summary>
    public string? Method { get; init; }

    public double? Confidence { get; init; }

    /// <summary>
    /// Every verse the row rests on, where one verse does not hold it: a passage in the order it
    /// stands (Hosea 1:2, 1:3, 1:4), or the two verses a composed statement joins. The first is
    /// <see cref="EntityRelationshipResponse.Reference"/>. Null for a row resting on one verse or none.
    /// </summary>
    public IList<VerseRefResponse>? Verses { get; init; }

    /// <summary>Which model or person says so.</summary>
    public string? Source { get; init; }

    /// <summary>
    /// Which declared dataset <see cref="Source"/> belongs to, so a page renders a credit rather
    /// than printing a sentence. Null where nothing claims the string, which is how an undeclared
    /// source is noticed instead of being silently credited to nobody.
    /// </summary>
    public string? Dataset => Datasets.Of(Source);

    /// <summary>
    /// The same fact as the other record states it, folded into this row: <em>Haran, father of
    /// Lot</em> under <em>Lot, son of Haran</em>, with its own verse and its own credit, so a client
    /// renders the fact once without having to work out which two rows were one.
    /// </summary>
    public IList<EntityRelationshipWitnessResponse> Corroboration { get; init; } = [];
}

/// <summary>
/// The other end of the relationship it hangs on — what it calls the relation, where it rests it,
/// and who says so.
/// </summary>
/// <param name="Type">The relation as the other record states it: <c>father-of</c> under <c>son-of</c>.</param>
/// <param name="Reference">
/// The verse it rests on, which is not always the one the row above rests on. Both citations are
/// real, so both are here rather than one standing for the fact.
/// </param>
/// <param name="Reversed">
/// True where it states the pair the other way about: Bani the ancestor of Adaiah, which is the row
/// above — <em>Adaiah, descendant of Bani</em> — read from the other end.
/// </param>
internal record EntityRelationshipWitnessResponse(
    string Type,
    string Category,
    bool Reversed,
    VerseRefResponse? Reference,
    string? Notes)
{
    public string? Method { get; init; }

    public double? Confidence { get; init; }

    public string? Source { get; init; }

    /// <summary>Which declared dataset <see cref="Source"/> belongs to, as on the row it corroborates.</summary>
    public string? Dataset => Datasets.Of(Source);
}
