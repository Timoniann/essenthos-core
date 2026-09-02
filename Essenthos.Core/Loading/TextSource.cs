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
internal sealed record WordDraft(
    string Surface,
    string Trailer,
    string? Lemma = null,
    string? StrongNumber = null,
    string? Gloss = null,
    string? Morphology = null);

/// <param name="Label">
/// The letter the edition prints after the number, where it prints one. Empty for the other
/// texts, which number their verses and nothing else.
/// </param>
internal sealed record VerseDraft(int Number, IReadOnlyList<WordDraft> Words, string Label = "");

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
