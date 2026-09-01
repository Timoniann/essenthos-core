using Essenthos.Core.Bhsa.Attributes;

namespace Essenthos.Core.Bhsa.Core;

public record HalfVerse(int SlotId, int Ordinal, HalfVersePart Part, IList<Word> Words);