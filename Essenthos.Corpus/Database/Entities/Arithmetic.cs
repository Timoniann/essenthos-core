using System.Text.Json;
using System.Text.Json.Serialization;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// The parts of a sentence of arithmetic this corpus works out itself, so it can be said in any
/// language: the events by their English names, the verses by book, chapter and verse, and the numbers.
/// </summary>
/// <param name="Kind">
/// <see cref="Counted"/>: <see cref="Event"/> is <see cref="From"/>'s year (year 1 where there is none)
/// with the <see cref="Terms"/> added. <see cref="Forward"/> and <see cref="Temple"/>: the base
/// reckoning's year moved by what the Greek of Genesis 5 and 11 adds before Abram, and on the Temple's
/// side less what 1 Kings 6:1 takes off.
/// </param>
/// <param name="Year">The year the arithmetic arrives at.</param>
public sealed record Arithmetic(string Kind, int Year)
{
    public const string Counted = "counted";
    public const string Forward = "forward";
    public const string Temple = "temple";

    public string? Event { get; init; }

    public string? From { get; init; }

    public int? FromYear { get; init; }

    public IReadOnlyList<ArithmeticTerm>? Terms { get; init; }

    /// <summary>The base reckoning's year of the event, which <see cref="Shift"/> moves.</summary>
    public int? Base { get; init; }

    public int? Shift { get; init; }

    /// <summary>The event whose move the shift is: Abram's birth.</summary>
    public string? Abram { get; init; }

    public int? AbramThere { get; init; }

    public int? AbramHere { get; init; }

    public int? Genealogies { get; init; }

    /// <summary>The years 1 Kings 6:1 takes off: <see cref="BaseTempleYears"/> less <see cref="TempleYears"/>.</summary>
    public int? Less { get; init; }

    public int? TempleYears { get; init; }

    public int? BaseTempleYears { get; init; }

    private static readonly JsonSerializerOptions Shape = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Write(Arithmetic arithmetic) => JsonSerializer.Serialize(arithmetic, Shape);

    public static Arithmetic? Read(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<Arithmetic>(json, Shape);
}

/// <summary>A number added or taken away, and the verse it is read from.</summary>
public sealed record ArithmeticTerm(int Sign, int Value, int Book, int Chapter, int Verse);
