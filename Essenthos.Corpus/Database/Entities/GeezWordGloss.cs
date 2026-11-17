using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What one Ge'ez word means where it stands, as a language model read it with the verse's Greek and an English
/// translation beside it: its dictionary form, its sense here, the whole word as a line under it, and the Greek words it
/// renders.
///
/// <para>
/// A model's reading and nothing more: no person checked the row, and the reader is told so wherever it is shown. It does
/// not say which entry of Dillmann's lexicon the word is a form of (<see cref="GeezLexiconEntry"/> is matched at read time
/// and says how), and its Greek is the model's choice among the verse's words, not a link of the mapping.
/// </para>
/// </summary>
public class GeezWordGloss
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long WordId { get; set; }

    public Word? Word { get; set; }

    /// <summary>The dictionary form, Dillmann's headword where one fits (<see cref="LemmaSource"/> <c>dillmann</c>), else the model's own.</summary>
    public required string Lemma { get; set; }

    public required string LemmaSource { get; set; }

    /// <summary>The lemma's sense in this verse, short: <em>rise, break camp</em>.</summary>
    public required string SenseEn { get; set; }

    public required string SenseUk { get; set; }

    /// <summary>The whole word, its particles and endings included, as a line under it reads: <em>and turn</em>.</summary>
    public required string LineEn { get; set; }

    public required string LineUk { get; set; }

    /// <summary>The words of the Greek beside it that this word renders, by their ids; empty where it renders none of them.</summary>
    public required long[] GreekWordIds { get; set; }

    /// <summary>What is written onto the stem and what form it is in: <em>wa- 'and' + imperative 2 m. pl.</em></summary>
    public string? Morphology { get; set; }

    /// <summary><c>high</c>, <c>medium</c> or <c>low</c>, as the model rated its own reading.</summary>
    public required string Confidence { get; set; }

    /// <summary>The model that read it, as its maker names it: <c>claude-opus-5-5</c>.</summary>
    public required string Model { get; set; }

    /// <summary>The run it came from, which names its prompt, its settings and its raw answers in the archive.</summary>
    public required string Run { get; set; }

    public required DateOnly Made { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"GeezWordGloss(word {WordId} {Lemma})";
}

/// <summary>
/// One sense of an entry of Dillmann's lexicon rendered from his Latin into the reader's languages by a language model,
/// in his order and under his number. A machine rendering of his Latin, shown under it and said to be one; his Latin
/// stays what the entry means.
/// </summary>
[PrimaryKey(nameof(Entry), nameof(Ordinal))]
public class GeezLexiconRendering
{
    /// <summary>The digital edition's identifier of the entry, as <see cref="GeezLexiconEntry.Entry"/>.</summary>
    public required string Entry { get; set; }

    /// <summary>The sense's place in the entry, from 1.</summary>
    public int Ordinal { get; set; }

    /// <summary>Dillmann's number for the sense, <c>2a</c>; <c>main</c> for the sense an entry opens with before any number.</summary>
    public required string Sense { get; set; }

    public string? En { get; set; }

    public string? Uk { get; set; }

    public string? De { get; set; }

    public string? Es { get; set; }

    public required string Model { get; set; }

    public required string Run { get; set; }

    public required DateOnly Made { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"GeezLexiconRendering({Entry} {Sense})";
}
