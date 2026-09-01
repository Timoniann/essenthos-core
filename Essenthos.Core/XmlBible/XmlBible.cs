namespace Essenthos.Core.XmlBible;

public class XmlBible
{
    public required string BibleName { get; init; }
    public required string Title { get; init; }
    public required string? Contributors { get; init; }
    public required string? Subject { get; init; }
    public required string? Creator { get; init; }
    public required string? Description { get; init; }
    public required string? Publisher { get; init; }
    public required string? Format { get; init; }
    public required string Language { get; init; }
    public required string Identifier { get; init; }
    public required string? Date { get; init; }
    public required string? Source { get; init; }
    public required string? Type { get; init; }
    public required string? Rights { get; init; }
    public required string? Coverage { get; init; }
    public List<XmlBibleBook> Books { get; init; } = [];
}

public class XmlBibleBook
{
    public required int BNumber { get; init; }
    public required string BName { get; init; }
    public required string BsName { get; init; }
    public List<XmlBibleChapter> Chapters { get; init; } = [];
}

public class XmlBibleChapter
{
    public required int CNumber { get; init; }
    public List<XmlBibleVerse> Verses { get; init; } = [];
}

public class XmlBibleVerse
{
    public required int VNumber { get; init; }
    public required string Text { get; init; }
}