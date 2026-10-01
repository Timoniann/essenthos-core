namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// How a word renders what its link sets opposite it, where it does not render it on its own.
///
/// <para>
/// A translation writes as words what an original writes as an inflection: <em>did</em> of <em>did
/// see</em> is the tense of εἴδομεν, <em>he</em> of <em>he said</em> the person of וַיֹּאמֶר's ending.
/// Such a word renders the same original word its head does, and saying so is a link like any other;
/// the role is what keeps it from reading as a second rendering of that word beside the head's, and
/// <see cref="LinkWord.HeadWordId"/> names the head.
/// </para>
/// </summary>
public enum LinkWordRole
{
    /// <summary>It writes its head's inflection or function: an auxiliary, a subject pronoun, <em>of</em> on a genitive.</summary>
    Attached,

    /// <summary>It renders one word together with its head: <em>out</em> of <em>went out</em>.</summary>
    PhraseMember,
}
