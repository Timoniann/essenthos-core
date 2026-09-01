using System.Xml.Linq;

namespace Essenthos.Core.Nestle;

public class NestleParser
{
    public List<NestleWord> Parse(string text, string? glossText)
    {
        var doc = XDocument.Parse(text);
        var root = doc.Root;
        if (root == null || root.Name.LocalName != "text")
        {
            throw new InvalidOperationException("Invalid XML format.");
        }

        Dictionary<string, string> glossMap = new();
        if (glossText != null)
        {
            var glossDoc = XDocument.Parse(glossText);
            var glossRoot = glossDoc.Root;
            if (glossRoot == null || glossRoot.Name.LocalName != "root")
            {
                throw new InvalidOperationException("Invalid XML format.");
            }

            foreach (var verseElement in glossRoot.Elements("verse"))
            {
                foreach (var wordElement in verseElement.Elements("w"))
                {
                    try
                    {
                        var osisId = wordElement.Attribute("osisId")!.Value;
                        var glossElement = wordElement.Element("gloss");
                        if (glossElement == null)
                        {
                            continue;
                        }

                        glossMap[osisId] = glossElement.Value;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw;
                    }
                }
            }
        }

        return root.Elements("w")
            .Select(w =>
            {
                var (word, trailer) = ParseWordAndTrailer(w.Value);
                var osisId = w.Attribute("osisId")!.Value;
                var firstDotIndex = osisId.IndexOf('.');
                var secondDotIndex = osisId.IndexOf('.', firstDotIndex + 1);
                // "!" symbol index
                var exclamationIndex = osisId.IndexOf('!');
                var book = osisId[..firstDotIndex];
                var chapter = int.Parse(osisId[(firstDotIndex + 1)..secondDotIndex]);
                var verse = int.Parse(osisId[(secondDotIndex + 1)..exclamationIndex]);
                var wordOrdinal = int.Parse(osisId[(exclamationIndex + 1)..]);
                var strongStr = w.Attribute("strong")!.Value;
                int strongNo;
                int? tenseVoiceMoodNumber;
                if (strongStr.Contains('&'))
                {
                    var index = strongStr.IndexOf('&');
                    strongNo = int.Parse(strongStr[..index]);
                    tenseVoiceMoodNumber = int.Parse(strongStr[(index + 1)..]);
                }
                else
                {
                    strongNo = int.Parse(strongStr);
                    tenseVoiceMoodNumber = null;
                }

                var lemma = w.Attribute("lemma")!.Value;
                var pos = w.Attribute("class")!.Value;
                var normalized = w.Attribute("normalized")!.Value;
                var caseStr = w.Attribute("case")?.Value;
                var number = w.Attribute("number")?.Value;
                var gender = w.Attribute("gender")?.Value;
                var form = w.Attribute("form")!.Value;
                var func = w.Attribute("func")!.Value;
                var mood = w.Attribute("mood")?.Value;
                var tense = w.Attribute("tense")?.Value;
                var voice = w.Attribute("voice")?.Value;
                var person = w.Attribute("person")?.Value;
                var gloss = glossMap.GetValueOrDefault(osisId, "");

                return new NestleWord
                {
                    Word = word,
                    Trailer = trailer,
                    OsisId = osisId,
                    Book = book,
                    Chapter = chapter,
                    Verse = verse,
                    WordOrdinal = wordOrdinal,
                    Lemma = lemma,
                    Pos = pos,
                    Normalized = normalized,
                    Strong = strongNo,
                    TenseVoiceMoodNumber = tenseVoiceMoodNumber,
                    Case = caseStr,
                    Number = number,
                    Gender = gender,
                    Form = form,
                    Func = func,
                    Mood = mood,
                    Tense = tense,
                    Voice = voice,
                    Person = person,
                    Gloss = gloss,
                };
            })
            .ToList();
    }

    internal static (string Word, string Trailer) ParseWordAndTrailer(string text)
    {
        var lastChar = text[^1];
        if (char.IsLetterOrDigit(lastChar))
        {
            return (text, " ");
        }

        var trailer = " ";
        try
        {
            for (var i = text.Length - 1; i >= 0; i--)
            {
                var c = text[i];
                if (!char.IsLetterOrDigit(c))
                {
                    continue;
                }

                // The slice ends after the letter at i, not before it — dropping it cost every
                // Greek word followed by punctuation its final character, and in Greek the final
                // character is the case ending.
                trailer = text[(i + 1)..] + " ";
                text = text[..(i + 1)];
                return (text, trailer);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw new Exception($"Failed to parse word: {text}. {e.Message}");
        }

        return (text, trailer);
    }
}