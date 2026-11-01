namespace Essenthos.Core.Corpus;

/// <summary>What said a person's sex, where the dataset did not: <c>entity.sex_source</c>.</summary>
internal static class SexSources
{
    /// <summary>A person's ruling, which outranks everything else that speaks of it.</summary>
    public const string Ruling = "ruling";

    /// <summary>The gendered word of the relationships the person is the subject of.</summary>
    public const string Relationship = "relationship";

    /// <summary>The Greek's grammar of the person's name.</summary>
    public const string Grammar = "grammar";
}
