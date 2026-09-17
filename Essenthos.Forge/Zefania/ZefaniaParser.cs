using System.Xml.Linq;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Zefania;

public class ZefaniaParser
{
    private static readonly List<(string Word, string Trailer)> CacheWordTrailer = new(20);

    public ZefaniaBible Parse(string content)
    {
        var doc = XDocument.Parse(content);
        var root = doc.Root;
        if (root == null || root.Name.LocalName != "XMLBIBLE")
        {
            throw new InvalidOperationException("Invalid XMLBIBLE format.");
        }

        var information = root.Element("INFORMATION");
        if (information == null)
        {
            throw new InvalidOperationException("INFORMATION element is required.");
        }

        return new ZefaniaBible
        {
            Books = root.Elements("BIBLEBOOK").Select(book =>
            {
                var bNumber = (int)book.Attribute("bnumber")!;
                var bName = book.Attribute("bname")!.Value;
                var bsName = book.Attribute("bsname")!.Value;
                return new ZefaniaBook
                {
                    Number = bNumber,
                    Name = bName,
                    ShortName = bsName,
                    Chapters = book.Elements("CHAPTER").Select(chapter =>
                    {
                        var cNumber = (int)chapter.Attribute("cnumber")!;
                        return new ZefaniaChapter
                        {
                            Number = cNumber,
                            Verses = chapter.Elements("VERS").Select(verse =>
                            {
                                var vNumber = (int)verse.Attribute("vnumber")!;
                                return new ZefaniaVerse
                                {
                                    Number = vNumber,
                                    Words = ParseVerseWords(verse)
                                };
                            }).ToList()
                        };
                    }).ToList()
                };
            }).ToList(),
        };
    }

    private static List<ZefaniaWord> ParseVerseWords(XElement verseElement, bool italic = false, bool red = false)
    {
        List<ZefaniaWord> result = [];
        foreach (var node in verseElement.Nodes())
        {
            switch (node)
            {
                case XText text:
                {
                    ProcessTextValue(text);
                    break;
                }
                case XElement { Name.LocalName: "gr" } element:
                    ProcessGrValue(element);
                    break;
                case XElement { Name.LocalName: "STYLE" } element:
                {
                    var css = element.Attribute("css")?.Value;
                    var newRed = false;
                    var newItalic = false;
                    if (css == "color:#ff0000")
                    {
                        newRed = true;
                    }
                    else if (css == "color:#808080;font-style:italic")
                    {
                        newItalic = true;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unexpected STYLE css '{css}' in verse.");
                    }

                    var childResult = ParseVerseWords(element, italic || newItalic, red || newRed);
                    result.AddRange(childResult);
                    break;
                }
                case XElement element:
                    throw new InvalidOperationException($"Unexpected element '{element.Name.LocalName}' in verse.");
                default:
                    throw new InvalidOperationException("Unexpected node type in verse.");
            }
        }

        if (result.Count != 0)
        {
            var lastWord = result[^1];
            if (!lastWord.Trailer.EndsWith(' '))
            {
                lastWord.Trailer += ' ';
            }
        }

        return result;

        void ProcessTextValue(XText text)
        {
            var textValue = text.Value.TrimStart();
            if (textValue.Length == 0)
            {
                return;
            }

            foreach (var (word, trailer) in ParseWordAndTrailer(textValue))
            {
                result.Add(new ZefaniaWord
                {
                    Number = result.Count + 1,
                    Text = word,
                    Trailer = trailer,
                    Italic = italic,
                    Red = red,
                });
            }
        }

        void ProcessGrValue(XElement element)
        {
            var strongNo = element.Attribute("str")!.Value;
            var textValue = element.Value.TrimStart();
            if (textValue.Length == 0)
            {
                return;
            }

            var nextText = (element.NextNode as XText)?.Value ?? (element.NextNode as XElement)?.Value;
            var nextTrailer = nextText == null ? null : TryGetBeginTrailer(nextText);

            var list = ParseWordAndTrailer(textValue);
            for (var i = 0; i < list.Count; i++)
            {
                var (word, trailer) = list[i];
                if (i == list.Count - 1 && nextTrailer != null)
                {
                    trailer = nextTrailer;
                }

                result.Add(new ZefaniaWord
                {
                    Number = result.Count + 1,
                    Text = word,
                    Trailer = trailer,
                    StrongNo = strongNo,
                    Italic = italic,
                    Red = red,
                });
            }
        }
    }

    // Trimming the whole string here used to discard the space that follows the punctuation, so
    // "him, and" came back as "him," and the rebuilt verse read "him,and".
    private static string? TryGetBeginTrailer(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsLetterOrDigit(c) || c == '\'' || c == '-')
            {
                return i == 0 ? null : WordSeparation.NormalizeWhitespace(text[..i]);
            }
        }

        return WordSeparation.NormalizeWhitespace(text);
    }

    private static List<(string Word, string Trailer)> ParseWordAndTrailer(string text)
    {
        var result = CacheWordTrailer;
        result.Clear();
        text = text.TrimStart();
        if (text.Length == 0)
        {
            return result;
        }

        var ci = 0;
        while (true)
        {
            var fc = text[ci];
            if (char.IsLetterOrDigit(fc) || fc == '\'' || fc == '-')
            {
                break;
            }

            ci++;
            if (ci >= text.Length)
            {
                return result;
            }
        }

        var length = text.Length;
        for (var i = ci; i < length; i++)
        {
            var start = i;
            char c;
            for (; i < length; i++)
            {
                c = text[i];
                if (!(char.IsLetter(c) || char.IsDigit(c) || c == '\'' || c == '-'))
                {
                    break;
                }
            }

            if (i >= length)
            {
                result.Add((text[start..], string.Empty));
                break;
            }

            var wordText = start == i ? string.Empty : text[start..i];
            var trailerStart = i++;
            for (; i < length; i++)
            {
                c = text[i];
                if (char.IsLetter(c) || char.IsDigit(c) || c == '\'' || c == '-')
                {
                    break;
                }
            }

            var trailer = text[trailerStart..i];
            result.Add((wordText, trailer));
            --i;
        }

        return result;
    }
}

public class ZefaniaBible
{
    public List<ZefaniaBook> Books { get; init; }
}

public class ZefaniaBook
{
    public required int Number { get; set; }

    public required string Name { get; set; }

    public required string ShortName { get; set; }

    public required List<ZefaniaChapter> Chapters { get; init; }
}

public class ZefaniaChapter
{
    public required int Number { get; set; }

    public required List<ZefaniaVerse> Verses { get; init; }
}

public class ZefaniaVerse
{
    public required int Number { get; set; }

    public required List<ZefaniaWord> Words { get; init; }
}

public class ZefaniaWord
{
    public required int Number { get; init; }

    public required string Text { get; init; }

    public required string Trailer { get; set; }

    public string? StrongNo { get; init; }

    public bool Italic { get; init; }

    public bool Red { get; init; }

    public override string ToString()
    {
        return $"ZWord('{Text}{Trailer}', StrongNo = {StrongNo}, Number = {Number}, Italic = {Italic}, Red = {Red})";
    }
}