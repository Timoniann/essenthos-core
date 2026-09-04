using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading;

/// <summary>
/// What a text is, before it is a row. Licence and provenance are part of the definition rather
/// than something filled in afterwards: a text whose licence nobody checked is a takedown waiting
/// for a project whose whole point is staying up, so <see cref="Validate"/> refuses to load one.
/// </summary>
internal sealed record TextDefinition(
    string Slug,
    string Name,
    string? NameNative,
    TextKind Kind,
    string Language,
    TextDirection Direction,
    Versification Versification,
    int? PublishedYear,
    string SourceUrl,
    string? RightsHolder,
    string Licence,
    string? LicenceUrl,
    Redistribution Redistribution,
    string? TextualFamily)
{
    /// <summary>
    /// What the licence requires be cited, where a name and a URL cannot carry it. Optional, and
    /// null means the licence asks for nothing beyond attribution — not that nobody checked.
    /// </summary>
    public string? Citation { get; init; }

    /// <summary>
    /// Who put the text into the language it is in — a person, or the body where there is no single
    /// person. Null is silence: nobody known, or not a translation.
    /// </summary>
    public string? Translators { get; init; }

    /// <summary>Who established this edition, which is rarely whoever translated it.</summary>
    public string? Editors { get; init; }

    /// <summary>Which edition or revision this is, where the year alone does not identify it.</summary>
    public string? Edition { get; init; }

    /// <summary>The year of the edition loaded, where it is not <see cref="PublishedYear"/>.</summary>
    public int? EditionYear { get; init; }

    /// <summary>What this text is and how it came to be, in a paragraph.</summary>
    public string? About { get; init; }

    /// <summary>What is unsettled or additional about the rights, beside the licence stated.</summary>
    public string? RightsNote { get; init; }


    public void Validate()
    {
        Require(Slug, nameof(Slug));
        Require(Name, nameof(Name));
        Require(Language, nameof(Language));

        Require(SourceUrl, nameof(SourceUrl),
            "where the text was obtained, so a reader can go back to what was loaded");
        Require(Licence, nameof(Licence),
            "the licence, checked before loading — an SPDX identifier where one applies, otherwise the " +
            "name the licensor uses");

        if (Redistribution == Redistribution.Unknown)
        {
            throw new InvalidOperationException(
                $"The text \"{Slug}\" does not say whether it may be served publicly. Set Redistribution to " +
                $"what the licence actually permits; Unknown is not public domain, it is nobody having looked.");
        }
    }

    private static void Require(string value, string name, string? what = null)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var explanation = what is null ? string.Empty : $" — {what}";
        throw new InvalidOperationException($"A text cannot be loaded without {name}{explanation}.");
    }
}

/// <param name="Morphology">
/// The annotation this text happens to carry, already serialised as JSON, because a word carries no
/// fixed set of features and half a million <c>JsonDocument</c>s is a cost with no reader.
/// </param>
/// <param name="Elided">
/// The source records this word and prints no letters for it. Set by the reader that knows why —
/// a Hebrew article that has assimilated into the preposition before it, a quotation mark that
/// opens a verse — so that an empty surface is a claim somebody made rather than a string that
/// happens to be empty, which is also what a broken tokeniser produces.
/// </param>
/// <param name="SuppliedSpan">
/// Which of its verse's supplied spans this word stands in, counting from one, where the edition
/// marks the words it supplies and its base text does not have. Null everywhere else, which is
/// silence rather than a claim: a text that marks nothing says nothing about any of its words.
/// </param>
internal sealed record WordDraft(
    string Surface,
    string Trailer,
    string? Lemma = null,
    string? StrongNumber = null,
    string? Gloss = null,
    string? Morphology = null,
    bool Elided = false,
    int? SuppliedSpan = null);

/// <param name="Chapter">The chapter of the edition's own numbering, which need not be the row's.</param>
/// <param name="Number">The verse of it.</param>
internal readonly record struct StatedNumberDraft(int Chapter, int Number);

/// <param name="Label">
/// The letter the edition prints after the number, where it prints one. Empty for the other
/// texts, which number their verses and nothing else.
/// </param>
internal sealed record VerseDraft(int Number, IReadOnlyList<WordDraft> Words, string Label = "")
{
    /// <summary>
    /// The addresses the edition prints for this verse in its own numbering, in the order it prints
    /// them. Empty for a text that numbers its verses the way it is stored and says nothing further
    /// — which is every text loaded here except the Synodal and Ohienko's Ukrainian, whose files
    /// were renumbered by their publisher and which say so verse by verse where it matters.
    /// </summary>
    public IReadOnlyList<StatedNumberDraft> Stated { get; init; } = [];
}

internal sealed record ChapterDraft(int Number, IReadOnlyList<VerseDraft> Verses);

/// <param name="CanonicalOrdinal">Its place in the shared order, 1 to 66, the same in every text.</param>
/// <param name="Position">
/// Its order within this text. BHSA orders the Tanakh so that its eighth book is 1 Samuel where the
/// canonical eighth is Ruth; reading one as the other made the API answer with the wrong book.
/// </param>
internal sealed record BookDraft(
    int CanonicalOrdinal,
    int Position,
    string Name,
    string Slug,
    IReadOnlyList<ChapterDraft> Chapters,
    string? NameNative = null,
    string? Abbreviation = null);

internal sealed record TextSource(TextDefinition Definition, IReadOnlyList<BookDraft> Books);
