namespace Essenthos.Core.Bhsa.Core;

public record Book(int SlotId, int Ordinal, string Name, IList<Chapter> Chapters);