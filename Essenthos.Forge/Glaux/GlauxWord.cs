namespace Essenthos.Core.Glaux;

/// <summary>
/// One annotated word of the GLAUx corpus: the form as the text writes it, and the lemma GLAUx
/// assigns it.
///
/// The corpus carries syntax, animacy and WordNet senses on the same element. None of that is read
/// here — the Septuagint's missing anchor is the lemma, and reading fields nothing consumes only
/// makes a 111 MB parse slower.
/// </summary>
/// <param name="Form">The inflected word, in Unicode NFD, exactly as GLAUx writes it.</param>
/// <param name="Lemma">The dictionary form GLAUx assigns, also NFD.</param>
/// <param name="PartOfSpeech">
/// The first character of the nine-position Ancient Greek Dependency Treebank tag, which is the
/// part of speech. Kept because a proper noun and a common noun that share a form are not the same
/// ambiguity, and a caller may want to say so.
/// </param>
internal readonly record struct GlauxWord(string Form, string Lemma, char PartOfSpeech);
