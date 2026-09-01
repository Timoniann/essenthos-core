namespace Essenthos.Core.Nestle;

public class NestleWord
{
    public required string Word { get; set; }
    public required string Trailer { get; set; }
    public required string OsisId { get; set; }
    public required string Book { get; set; }
    public required int Chapter { get; set; }
    public required int Verse { get; set; }
    public required int WordOrdinal { get; set; }
    public required string Pos { get; set; }
    public required string Lemma { get; set; }
    public required string Normalized { get; set; }
    public required int Strong { get; set; }
    public int? TenseVoiceMoodNumber { get; set; }
    public string? Case { get; set; }
    public string? Number { get; set; }
    public string? Gender { get; set; }
    public required string Form { get; set; }
    public required string Func { get; set; }
    public string? Mood { get; set; }
    public string? Tense { get; set; }
    public string? Voice { get; set; }
    public string? Person { get; set; }
    public required string Gloss { get; set; }
}