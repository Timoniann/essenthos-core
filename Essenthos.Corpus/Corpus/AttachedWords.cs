namespace Essenthos.Core.Corpus;

/// <summary>
/// Which words of the corpus's links write another word's inflection or function, and which word
/// that is, as EVIDENTIA's attached-word verdicts say.
///
/// <para>
/// An attached word is written as an ordinary link from the word to what its rule placed it on, so
/// the links say <em>did</em> renders εἴδομεν and say nothing of <em>see</em>. Its decision knows
/// the rest: the attachment opens its rationale (<c>AuxiliaryVerb of 'see'</c>), and since the
/// decision records the pair it rests on, the head's word too. A word is attached only where it
/// landed on its head's own counterpart; one placed on a part of its own, <em>and</em> on the ו
/// before its head's rendering, renders that part and is an ordinary link.
/// </para>
///
/// <para>
/// The build marks the role and the head from the ledger (<c>evidentia-pairs</c>), where the run kept the head's pair or
/// names the head by the surface in its rationale.
/// </para>
/// </summary>
internal static class AttachedWords
{
    /// <summary>The decision kind EVIDENTIA's attached-word resolver records.</summary>
    public const string DecisionKind = "attached-word";

    /// <summary>The attachments by which a word renders one word together with its head, rather than writing its inflection.</summary>
    public static readonly IReadOnlyList<string> PhraseAttachments = ["PhrasalParticle", "VerbOfItsParticle"];
}
