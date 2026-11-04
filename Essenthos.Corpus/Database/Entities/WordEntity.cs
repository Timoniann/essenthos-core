using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A statement that this word of this text names this person or this place.
///
/// <see cref="EntityVerse"/> already says that an entity is named somewhere in a verse, and that is
/// as far as any of the datasets go. A reader hovering a word wants the answer at the word, and a
/// verse naming four people cannot give it: the encyclopedia and the text have been two halves of
/// this project that never met, and this is the join between them.
///
/// <para>
/// **It is an assertion like every other one in this corpus, so it carries what asserted it.** A
/// bare foreign key from a word to an entity would light the reader's card up and would be the
/// wrong answer, because the same column would then hold a source's testimony, a resolution through
/// a lexicon and eventually a model's reading of the context, with nothing to tell them apart. The
/// old schema did exactly that — <c>EntityType</c>, <c>EntityId</c> and <c>EntitySlug</c> on the
/// word, no provenance — and the land of Canaan came out annotated as the person Canaan with
/// nothing on the row to say the annotation was ever an inference.
/// </para>
///
/// <para>
/// So this follows <see cref="Link"/> and <see cref="LinkClaim"/> rather than inventing a second
/// vocabulary for the same idea: the row here is the conclusion and holds the strongest claim's
/// method, confidence and source, and <see cref="WordEntityClaim"/> holds every method that says
/// so. The same provenance constraints apply — a claim a source states carries no confidence, one a
/// process inferred carries one, and every row names what produced it.
/// </para>
///
/// <para>
/// **A word may carry more than one row and that is not a defect.** Two methods proposing two
/// different people for one word is the honest record of a disagreement, and the reader resolves it
/// by standing — a person's correction outranks a lexicon's resolution — or shows nothing where two
/// methods of equal standing disagree. Refusing to answer is the correct answer for the twenty-three
/// men called Zechariah, and it has to be expressible.
/// </para>
/// </summary>
[Index(nameof(EntityId))]
[Index(nameof(WordId), nameof(EntityId), IsUnique = true)]
public class WordEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long WordId { get; set; }

    public Word? Word { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>
    /// Null where a source or a person states that this word names this entity, a number in 0..1
    /// where a process concluded it. Everything phase one writes is the second kind: no dataset here
    /// annotates a word, so every row of it is this corpus reasoning and says so.
    /// </summary>
    public double? Confidence { get; set; }

    /// <summary>The file, the reasoning and its version, or the person. Never empty.</summary>
    public required string Source { get; set; }

    /// <summary>The route, where it is worth reading — the number it went through, or the word.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// How the word stands for the record (<see cref="WordEntityRoles"/>): its name, a people, a title, a thing. One word
    /// may stand for several records, each in its own role. Null on a row the corpus load wrote before the names projection.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Where the name on this word comes from (<see cref="WordEntityOrigins"/>). A <c>base-reading</c> row is not the word's
    /// own name: it is the record the base text names where the translation names another (the <c>differs</c> row on the
    /// same word), shown second. Null on a row the corpus load wrote before the names projection.
    /// </summary>
    public string? Origin { get; set; }

    public ICollection<WordEntityClaim> Claims { get; set; } = [];

    public override string ToString() => $"WordEntity(word {WordId} names {EntityId}, {Method})";
}

/// <summary>
/// One method saying that a word names an entity, and how sure it is.
///
/// The reason it is a table and not three more columns is the reason <see cref="LinkClaim"/> is:
/// two methods that share no reasoning landing on the same answer is the cheapest evidence there
/// is, and with one method per row the second one has nowhere to go and the first to speak wins.
/// Here that matters more than it does for links, because the methods are so unequal — a Strong
/// number that names exactly one person, a model's reading of the surrounding verses, and a person
/// who checked. Flattened into one column they are indistinguishable; kept apart, a word two of them
/// agree on is visibly better evidenced than a word only one proposed.
/// </summary>
[Index(nameof(WordEntityId), nameof(Method), nameof(Source), IsUnique = true)]
public class WordEntityClaim
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long WordEntityId { get; set; }

    public WordEntity? WordEntity { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>
    /// How sure this method is, and null exactly when a source stated it or a person set it. The
    /// database enforces both directions, the same way it does for a link's claims.
    /// </summary>
    public double? Confidence { get; set; }

    public required string Source { get; set; }

    public string? Note { get; set; }

    public override string ToString() => $"WordEntityClaim({Method} on annotation {WordEntityId})";
}

/// <summary>The roles a word stands for a record in, as <c>word_entity.role</c> holds them.</summary>
public static class WordEntityRoles
{
    /// <summary>The word is the record's name, or stands for the person or place where it is written.</summary>
    public const string Name = "name";

    /// <summary>The word names a people, a tribe or a nation.</summary>
    public const string People = "people";

    /// <summary>The word is a title, and the record the title or the one who bears it.</summary>
    public const string Title = "title";

    /// <summary>The word names an object, a feast, a term.</summary>
    public const string Thing = "thing";

    public static readonly IReadOnlyList<string> All = [Name, People, Title, Thing];
}

/// <summary>Where a name on a word comes from, as <c>word_entity.origin</c> holds it.</summary>
public static class WordEntityOrigins
{
    /// <summary>A reference text's own settled name.</summary>
    public const string Settled = "settled";

    /// <summary>Carried from the base text over a link of the final mapping.</summary>
    public const string Projected = "projected";

    /// <summary>An override's addition.</summary>
    public const string Added = "added";

    /// <summary>The translation's own record where it names another than its base (an override's differs): the first card.</summary>
    public const string Differs = "differs";

    /// <summary>The base's record on a word whose translation names another: the second card, never the word's own name.</summary>
    public const string BaseReading = "base-reading";

    public static readonly IReadOnlyList<string> All = [Settled, Projected, Added, Differs, BaseReading];
}
