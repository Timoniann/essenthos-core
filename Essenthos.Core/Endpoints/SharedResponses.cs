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

internal record CoverageResponse(int FirstBook, int LastBook);

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
    CoverageResponse Coverage);

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
    string? Language);

internal record TextVerseResponse(int Number, IList<TextWordResponse> Words);

internal record VerseRefResponse(int BookOrdinal, string Book, string Slug, int Chapter, int Verse);
