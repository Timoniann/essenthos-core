using System.Xml.Linq;

namespace Essenthos.Core.XmlBible;

public class XmlBibleParser
{
    public XmlBible Parse(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);
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

        var bibleBooks = root.Elements("BIBLEBOOK")
            .Select(b => new XmlBibleBook
            {
                BNumber = (int)b.Attribute("bnumber")!,
                BName = b.Attribute("bname")?.Value ??
                        throw new InvalidOperationException("Book name is required."),
                BsName = b.Attribute("bsname")?.Value ??
                         throw new InvalidOperationException("Short book name is required."),
                Chapters = b.Elements("CHAPTER")
                    .Select(c => new XmlBibleChapter
                    {
                        CNumber = (int)c.Attribute("cnumber")!,
                        Verses = c.Elements("VERS")
                            .Select(v => new XmlBibleVerse
                            {
                                VNumber = (int)v.Attribute("vnumber")!,
                                Text = v.Value
                            }).ToList()
                    }).ToList()
            }).ToList();

        var bible = new XmlBible
        {
            BibleName = root.Attribute("biblename")?.Value ??
                        throw new InvalidOperationException("Bible name is required."),
            Title = information.Element("title")?.Value ??
                    throw new InvalidOperationException("Title is required."),
            Contributors = information.Element("contributors")?.Value,
            Subject = information.Element("subject")?.Value,
            Creator = information.Element("creator")?.Value,
            Description = information.Element("description")?.Value,
            Publisher = information.Element("publisher")?.Value,
            Format = information.Element("format")?.Value,
            Language = information.Element("language")?.Value ??
                       throw new InvalidOperationException("Language is required."),
            Identifier = information.Element("identifier")?.Value ??
                         throw new InvalidOperationException("Identifier is required."),
            Date = information.Element("date")?.Value,
            Source = information.Element("source")?.Value,
            Type = information.Element("type")?.Value,
            Rights = information.Element("rights")?.Value,
            Coverage = information.Element("coverage")?.Value,
            Books = bibleBooks
        };
        return bible;
    }
}