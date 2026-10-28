using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What a clause says of a woman, where the language says it of her in the feminine: Sarah is
/// <em>похована у Махпелі</em>, not <em>похований</em>, Rahab <em>жила у Єрихоні</em>, Deborah a
/// <em>пророчиця</em>, a <em>Prophetin</em> and a <em>profetisa</em>. And what it says of a people,
/// which agrees with a plural: the Anakim <em>жили у Хевроні</em>, <em>wohnten in Hebron</em> and
/// <em>habitaron en Hebrón</em>, and are the <em>descendants of Anak</em>.
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
    /// The phrasing for this relation said of this subject: the plural where the subject is a people
    /// and the language agrees with it, the feminine where the subject is a woman and the language has
    /// one; null where the table's own phrasing stands.
    /// </summary>
    public static Phrasing? OfSubject(string? language, string relation, string? sex, bool people) =>
        language is null ? null
        : people ? OfAPeople.GetValueOrDefault((language, relation))
        : sex == SharedParents.Female ? Feminine.GetValueOrDefault((language, relation))
        : null;

    /// <summary>
    /// The relations a people is said to have whose words agree with their subject, in the plural. A
    /// relation absent here is said one way of one and of many: <em>з племені</em>,
    /// <em>begraben in</em>, <em>Förderer</em>, and the plural <c>struck-by</c> and
    /// <c>descendants-of</c>, which are said of a people only.
    /// </summary>
    internal static IReadOnlyDictionary<(string Language, string Relation), Phrasing> OfAPeople { get; } =
        new Dictionary<(string, string), Phrasing>
        {
            [(English, DescriptorRelations.AncestorOf)] = new("ancestors of ", Nominative),
            [(English, DescriptorRelations.DescendantOf)] = new("descendants of ", Nominative),
            [(English, DescriptorRelations.ServantOf)] = new("servants of ", Nominative),
            [(English, DescriptorRelations.AllyOf)] = new("allies of ", Nominative),
            [(English, DescriptorRelations.SupporterOf)] = new("supporters of ", Nominative),
            [(English, DescriptorRelations.CompanionOf)] = new("companions of ", Nominative),
            [(English, DescriptorRelations.FounderOf)] = new("founders of ", Nominative),
            [(English, DescriptorRelations.HeirOf)] = new("heirs of ", Nominative),
            [(English, DescriptorRelations.LeaderOf)] = new("leaders of ", Nominative),
            [(English, DescriptorRelations.MasterOf)] = new("masters of ", Nominative),
            [(English, DescriptorRelations.DiscipleOf)] = new("disciples of ", Nominative),
            [(English, DescriptorRelations.KillerOf)] = new("killers of ", Nominative),

            [(Ukrainian, DescriptorRelations.LivedIn)] = new("жили у ", Locative),
            [(Ukrainian, DescriptorRelations.BuriedIn)] = new("поховані у ", Locative),
            [(Ukrainian, DescriptorRelations.SupportedBy)] = new("мали підтримку від ", Genitive),
            [(Ukrainian, DescriptorRelations.ChosenInPlaceOf)] = new("обрані замість ", Genitive),
            [(Ukrainian, DescriptorRelations.AncestorOf)] = new("предки ", Genitive),
            [(Ukrainian, DescriptorRelations.DescendantOf)] = new("нащадки ", Genitive),
            [(Ukrainian, DescriptorRelations.ServantOf)] = new("слуги ", Genitive),
            [(Ukrainian, DescriptorRelations.AllyOf)] = new("союзники ", Genitive),
            [(Ukrainian, DescriptorRelations.SupporterOf)] = new("покровителі ", Genitive),
            [(Ukrainian, DescriptorRelations.CompanionOf)] = new("товариші ", Genitive),
            [(Ukrainian, DescriptorRelations.FounderOf)] = new("засновники ", Genitive),
            [(Ukrainian, DescriptorRelations.HeirOf)] = new("спадкоємці ", Genitive),
            [(Ukrainian, DescriptorRelations.LeaderOf)] = new("вожді ", Genitive),
            [(Ukrainian, DescriptorRelations.MasterOf)] = new("господарі ", Genitive),
            [(Ukrainian, DescriptorRelations.DiscipleOf)] = new("учні ", Genitive),
            [(Ukrainian, DescriptorRelations.KillerOf)] = new("вбивці ", Genitive),

            [(German, DescriptorRelations.LivedIn)] = new("wohnten in ", Locative),
            [(German, DescriptorRelations.AncestorOf)] = new("Vorfahren ", Genitive),
            [(German, DescriptorRelations.DescendantOf)] = new("Nachkommen ", Genitive),
            [(German, DescriptorRelations.ServantOf)] = new("Knechte ", Genitive),
            [(German, DescriptorRelations.AllyOf)] = new("Verbündete ", Genitive),
            [(German, DescriptorRelations.CompanionOf)] = new("Gefährten ", Genitive),
            [(German, DescriptorRelations.HeirOf)] = new("Erben ", Genitive),
            [(German, DescriptorRelations.MasterOf)] = new("Herren ", Genitive),
            [(German, DescriptorRelations.KillerOf)] = new("töteten ", Nominative),

            [(Spanish, DescriptorRelations.LivedIn)] = new("habitaron en ", Nominative),
            [(Spanish, DescriptorRelations.BuriedIn)] = new("sepultados en ", Nominative),
            [(Spanish, DescriptorRelations.SupportedBy)] = new("sostenidos por ", Nominative),
            [(Spanish, DescriptorRelations.CreatedBy)] = new("creados por ", Nominative),
            [(Spanish, DescriptorRelations.ExiledBy)] = new("llevados al exilio por ", Nominative),
            [(Spanish, DescriptorRelations.ChosenInPlaceOf)] = new("elegidos en lugar de ", Nominative),
            [(Spanish, DescriptorRelations.AncestorOf)] = new("antepasados de ", Nominative),
            [(Spanish, DescriptorRelations.DescendantOf)] = new("descendientes de ", Nominative),
            [(Spanish, DescriptorRelations.ServantOf)] = new("siervos de ", Nominative),
            [(Spanish, DescriptorRelations.AllyOf)] = new("aliados de ", Nominative),
            [(Spanish, DescriptorRelations.SupporterOf)] = new("benefactores de ", Nominative),
            [(Spanish, DescriptorRelations.CompanionOf)] = new("compañeros de ", Nominative),
            [(Spanish, DescriptorRelations.FounderOf)] = new("fundadores de ", Nominative),
            [(Spanish, DescriptorRelations.HeirOf)] = new("herederos de ", Nominative),
            [(Spanish, DescriptorRelations.LeaderOf)] = new("líderes de ", Nominative),
            [(Spanish, DescriptorRelations.MasterOf)] = new("amos de ", Nominative),
            [(Spanish, DescriptorRelations.DiscipleOf)] = new("discípulos de ", Nominative),
            [(Spanish, DescriptorRelations.KillerOf)] = new("dieron muerte a ", Nominative),
        };

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
            [(Ukrainian, DescriptorRelations.MemberOf)] = new("одна з ", Genitive),
            [(Ukrainian, DescriptorRelations.AllyOf)] = new("союзниця ", Genitive),
            [(Ukrainian, DescriptorRelations.FounderOf)] = new("засновниця ", Genitive),
            [(Ukrainian, DescriptorRelations.LeaderOf)] = new("провідниця ", Genitive),
            [(Ukrainian, DescriptorRelations.SupporterOf)] = new("покровителька ", Genitive),
            [(Ukrainian, DescriptorRelations.HeirOf)] = new("спадкоємиця ", Genitive),
            [(Ukrainian, DescriptorRelations.CousinOf)] = new("двоюрідна родичка ", Genitive),
            [(Ukrainian, DescriptorRelations.ChosenInPlaceOf)] = new("обрана замість ", Genitive),

            // Прамати rather than a feminine of предок: the word Ukrainian has for a woman a
            // line descends from.
            [(Ukrainian, DescriptorRelations.AncestorOf)] = new("прамати ", Genitive),

            [(German, DescriptorRelations.ProphetTo)] = new("Prophetin ", Genitive),
            [(German, DescriptorRelations.ServantOf)] = new("Magd ", Genitive),
            [(German, DescriptorRelations.MasterOf)] = new("Herrin ", Genitive),
            [(German, DescriptorRelations.DiscipleOf)] = new("Jüngerin ", Genitive),
            [(German, DescriptorRelations.CompanionOf)] = new("Gefährtin ", Genitive),
            [(German, DescriptorRelations.TeacherOf)] = new("Lehrerin ", Genitive),
            [(German, DescriptorRelations.MemberOf)] = new("eine ", Genitive),
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
            [(Spanish, DescriptorRelations.MemberOf)] = new("una de ", Nominative),
            [(Spanish, DescriptorRelations.AllyOf)] = new("aliada de ", Nominative),
            [(Spanish, DescriptorRelations.FounderOf)] = new("fundadora de ", Nominative),
            [(Spanish, DescriptorRelations.SupporterOf)] = new("benefactora de ", Nominative),
            [(Spanish, DescriptorRelations.CreatorOf)] = new("creadora de ", Nominative),
            [(Spanish, DescriptorRelations.HeirOf)] = new("heredera de ", Nominative),
            [(Spanish, DescriptorRelations.CousinOf)] = new("prima de ", Nominative),
            [(Spanish, DescriptorRelations.AncestorOf)] = new("antepasada de ", Nominative),
            [(Spanish, DescriptorRelations.ChosenInPlaceOf)] = new("elegida en lugar de ", Nominative),
        };
}
