using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What a clause says of a woman, where the language says it of her in the feminine: Sarah is
/// <em>похована у Махпелі</em>, not <em>похований</em>, Rahab <em>жила у Єрихоні</em>, Deborah a
/// <em>пророчиця</em>, a <em>Prophetin</em> and a <em>profetisa</em>.
/// </summary>
/// <remarks>
/// <para>
/// The kinship relations are already split by sex — <c>son-of</c> and <c>daughter-of</c> — because
/// the claim itself says which. The rest are not, and should not be: <c>buried-in</c> is the same
/// fact of a man and a woman. What differs is how the language agrees with the person the line is
/// about, so the feminine is chosen here, by the subject's sex, and the vocabulary stays closed.
/// </para>
///
/// <para>
/// Each feminine puts the name in the case its masculine does, so a line that lists names under one
/// phrase reads the same forms whichever it says. A relation absent here is said one way of both:
/// <em>суддя</em>, <em>вбивця</em>, <em>líder</em>, <em>begraben in</em> and every German
/// participle, which does not agree after its noun. English is absent altogether.
/// </para>
/// </remarks>
internal static partial class DescriptorPhrasings
{
    /// <summary>
    /// The phrasing for this relation said of a subject of this sex, where the language has a feminine
    /// for it and the subject is a woman; null where the table's own phrasing stands.
    /// </summary>
    public static Phrasing? OfSubject(string? language, string relation, string? sex) =>
        language is not null && sex == SharedParents.Female
                             && Feminine.TryGetValue((language, relation), out var phrasing)
            ? phrasing
            : null;

    internal static IReadOnlyDictionary<(string Language, string Relation), Phrasing> Feminine { get; } =
        new Dictionary<(string, string), Phrasing>
        {
            [(Ukrainian, DescriptorRelations.LivedIn)] = new("жила у ", Locative),
            [(Ukrainian, DescriptorRelations.BuriedIn)] = new("похована у ", Locative),
            [(Ukrainian, DescriptorRelations.KilledBy)] = new("загинула від руки ", Genitive),
            [(Ukrainian, DescriptorRelations.SupportedBy)] = new("мала підтримку від ", Genitive),
            [(Ukrainian, DescriptorRelations.ProphetTo)] = new("пророчиця ", Genitive),
            [(Ukrainian, DescriptorRelations.ServantOf)] = new("служниця ", Genitive),
            [(Ukrainian, DescriptorRelations.MasterOf)] = new("господиня ", Genitive),
            [(Ukrainian, DescriptorRelations.DiscipleOf)] = new("учениця ", Genitive),
            [(Ukrainian, DescriptorRelations.CompanionOf)] = new("супутниця ", Genitive),
            [(Ukrainian, DescriptorRelations.TeacherOf)] = new("учителька ", Genitive),
            [(Ukrainian, DescriptorRelations.AllyOf)] = new("союзниця ", Genitive),
            [(Ukrainian, DescriptorRelations.FounderOf)] = new("засновниця ", Genitive),
            [(Ukrainian, DescriptorRelations.LeaderOf)] = new("провідниця ", Genitive),
            [(Ukrainian, DescriptorRelations.SupporterOf)] = new("покровителька ", Genitive),
            [(Ukrainian, DescriptorRelations.HeirOf)] = new("спадкоємиця ", Genitive),
            [(Ukrainian, DescriptorRelations.CousinOf)] = new("двоюрідна родичка ", Genitive),

            // Прамати rather than a feminine of предок: the word Ukrainian has for a woman a
            // line descends from.
            [(Ukrainian, DescriptorRelations.AncestorOf)] = new("прамати ", Genitive),

            [(German, DescriptorRelations.ProphetTo)] = new("Prophetin für ", Nominative),
            [(German, DescriptorRelations.ServantOf)] = new("Magd ", Genitive),
            [(German, DescriptorRelations.MasterOf)] = new("Herrin ", Genitive),
            [(German, DescriptorRelations.DiscipleOf)] = new("Jüngerin ", Genitive),
            [(German, DescriptorRelations.CompanionOf)] = new("Gefährtin ", Genitive),
            [(German, DescriptorRelations.TeacherOf)] = new("Lehrerin ", Genitive),
            [(German, DescriptorRelations.AllyOf)] = new("Verbündete ", Genitive),
            [(German, DescriptorRelations.FounderOf)] = new("Gründerin ", Genitive),
            [(German, DescriptorRelations.LeaderOf)] = new("Anführerin ", Genitive),
            [(German, DescriptorRelations.JudgeOf)] = new("Richterin ", Genitive),
            [(German, DescriptorRelations.PriestOf)] = new("Priesterin ", Genitive),
            [(German, DescriptorRelations.SupporterOf)] = new("Förderin ", Genitive),
            [(German, DescriptorRelations.CreatorOf)] = new("Schöpferin ", Genitive),
            [(German, DescriptorRelations.HeirOf)] = new("Erbin ", Genitive),
            [(German, DescriptorRelations.CousinOf)] = new("Cousine ", Genitive),
            [(German, DescriptorRelations.AncestorOf)] = new("Vorfahrin ", Genitive),

            // The relative pronoun agrees with the woman whose heir it was: deren, not dessen.
            [(German, DescriptorRelations.InheritedBy)] = new("deren Erbe war ", Nominative),

            [(Spanish, DescriptorRelations.BuriedIn)] = new("sepultada en ", Nominative),
            [(Spanish, DescriptorRelations.KilledBy)] = new("muerta a manos de ", Nominative),
            [(Spanish, DescriptorRelations.SupportedBy)] = new("sostenida por ", Nominative),
            [(Spanish, DescriptorRelations.CreatedBy)] = new("creada por ", Nominative),
            [(Spanish, DescriptorRelations.ExiledBy)] = new("llevada al exilio por ", Nominative),
            [(Spanish, DescriptorRelations.ProphetTo)] = new("profetisa de ", Nominative),
            [(Spanish, DescriptorRelations.PriestOf)] = new("sacerdotisa de ", Nominative),
            [(Spanish, DescriptorRelations.JudgeOf)] = new("jueza de ", Nominative),
            [(Spanish, DescriptorRelations.ServantOf)] = new("sierva de ", Nominative),
            [(Spanish, DescriptorRelations.MasterOf)] = new("ama de ", Nominative),
            [(Spanish, DescriptorRelations.DiscipleOf)] = new("discípula de ", Nominative),
            [(Spanish, DescriptorRelations.CompanionOf)] = new("compañera de ", Nominative),
            [(Spanish, DescriptorRelations.TeacherOf)] = new("maestra de ", Nominative),
            [(Spanish, DescriptorRelations.AllyOf)] = new("aliada de ", Nominative),
            [(Spanish, DescriptorRelations.FounderOf)] = new("fundadora de ", Nominative),
            [(Spanish, DescriptorRelations.SupporterOf)] = new("benefactora de ", Nominative),
            [(Spanish, DescriptorRelations.CreatorOf)] = new("creadora de ", Nominative),
            [(Spanish, DescriptorRelations.HeirOf)] = new("heredera de ", Nominative),
            [(Spanish, DescriptorRelations.CousinOf)] = new("prima de ", Nominative),
            [(Spanish, DescriptorRelations.AncestorOf)] = new("antepasada de ", Nominative),
        };
}
