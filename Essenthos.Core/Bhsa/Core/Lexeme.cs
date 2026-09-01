using Essenthos.Core.Bhsa.Attributes;

namespace Essenthos.Core.Bhsa.Core;

public record Lexeme(
    int SlotId,
    string TextUtf8,
    string Gloss,
    string VocalizedLexemeUtf8,
    LanguageIso Language,
    PartOfSpeech PartOfSpeech,
    LexicalSet? LexicalSet,
    Nametype[] Nametypes,
    IList<Word> Words
);