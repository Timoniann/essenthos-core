using System.Diagnostics.CodeAnalysis;
using Essenthos.Core.Bhsa;

namespace Essenthos.Core.TextFabric;

[SuppressMessage("ReSharper", "ReplaceSubstringWithRangeIndexer")]
[SuppressMessage("ReSharper", "ReplaceSliceWithRangeIndexer")]
public class DocumentParser
{
    public IDocument Parse(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var fileLength = new FileInfo(filePath).Length;
        var approximateLineCount = Math.Max(0, (int)((fileLength - 50 * 15) / 4));
        var streamReader = new StreamReader(filePath, System.Text.Encoding.UTF8, true);

        var index = 0;
        var metadata = ParseMetadata(streamReader, out var lineIndex);
        switch (metadata.ValueType)
        {
            case DocumentValueType.String:
            {
                var items = new Dictionary<int, string>(approximateLineCount);
                var rangeItems = new List<(RangeInt, string)>();
                while (!streamReader.EndOfStream)
                {
                    var lineValue = streamReader.ReadLine()!;
                    ++lineIndex;
                    if (lineValue.Length == 0)
                    {
                        items.Add(++index, lineValue);
                        continue;
                    }

                    var spaceIndex = lineValue.IndexOf('\t');
                    if (spaceIndex == -1)
                    {
                        items.Add(++index, lineValue);
                        continue;
                    }

                    var numberStringSpan = lineValue.AsSpan(0, spaceIndex);
                    var rangeKeyIndex = numberStringSpan.IndexOf('-');
                    if (rangeKeyIndex == -1)
                    {
                        if (!int.TryParse(numberStringSpan, out var number))
                        {
                            throw new FormatException(
                                $"Line {lineIndex + 1} is not a valid TextFabric line: {lineValue}. Invalid number '{numberStringSpan.ToString()}'.");
                        }

                        index = number;
                        lineValue = lineValue.Substring(spaceIndex + 1);
                        items.Add(index, lineValue);
                        continue;
                    }

                    var rangePart1 = numberStringSpan.Slice(0, rangeKeyIndex);
                    var rangePart2 = numberStringSpan.Slice(rangeKeyIndex + 1);
                    if (!int.TryParse(rangePart1, out var start) ||
                        !int.TryParse(rangePart2, out var end) || start > end)
                    {
                        throw new FormatException(
                            $"Line {lineIndex + 1} is not a valid TextFabric line: {lineValue}. Invalid range '{numberStringSpan.ToString()}'.");
                    }

                    var item = lineValue.Substring(spaceIndex + 1);
                    rangeItems.Add((new RangeInt(start, end), item));
                    index = end;
                }

                return new Document<string>(name, metadata, items, rangeItems);
            }
            case DocumentValueType.Integer:
            {
                var items = new Dictionary<int, int>(approximateLineCount);
                var rangeItems = new List<(RangeInt, int)>();
                while (!streamReader.EndOfStream)
                {
                    var lineValue = streamReader.ReadLine()!;
                    ++lineIndex;
                    if (lineValue.Length == 0)
                    {
                        items.Add(++index, int.Parse(lineValue));
                        continue;
                    }

                    var spaceIndex = lineValue.IndexOf('\t');
                    if (spaceIndex == -1)
                    {
                        items.Add(++index, int.Parse(lineValue));
                        continue;
                    }

                    var numberStringSpan = lineValue.AsSpan(0, spaceIndex);
                    var rangeKeyIndex = numberStringSpan.IndexOf('-');
                    if (rangeKeyIndex == -1)
                    {
                        if (!int.TryParse(numberStringSpan, out var number))
                        {
                            throw new FormatException(
                                $"Line {lineIndex + 1} is not a valid TextFabric line: {lineValue}. Invalid number '{numberStringSpan.ToString()}'.");
                        }

                        index = number;
                        items.Add(index, int.Parse(lineValue.AsSpan(spaceIndex + 1)));
                        continue;
                    }


                    var rangePart1 = numberStringSpan.Slice(0, rangeKeyIndex);
                    var rangePart2 = numberStringSpan.Slice(rangeKeyIndex + 1);
                    if (!int.TryParse(rangePart1, out var start) ||
                        !int.TryParse(rangePart2, out var end) || start > end)
                    {
                        throw new FormatException(
                            $"Line {lineIndex + 1} is not a valid TextFabric line: {lineValue}. Invalid range '{numberStringSpan.ToString()}'.");
                    }

                    var item = int.Parse(lineValue.AsSpan(spaceIndex + 1));
                    rangeItems.Add((new RangeInt(start, end), item));
                    index = end;
                }

                return new Document<int>(name, metadata, items, rangeItems);
            }
            default:
                throw new FormatException($"Unsupported value type: {metadata.ValueType}");
        }
    }

    private static DocumentMetadata ParseMetadata(StreamReader reader, out int lineIndex)
    {
        if (reader.EndOfStream)
        {
            throw new FormatException("No metadata found in the file.");
        }

        var firstLine = reader.ReadLine()!;
        if (!firstLine.StartsWith('@'))
        {
            throw new FormatException($"Not valid TextFabric metadata line: {firstLine}");
        }

        var type = firstLine switch
        {
            "@node" => DocumentType.Node,
            "@edge" => DocumentType.Edge,
            "@config" => DocumentType.Config,
            _ => throw new FormatException($"Not valid TextFabric metadata line: {firstLine}")
        };
        DocumentMetadata.Builder builder = new()
        {
            DocumentType = type,
            ValueType = DocumentValueType.String
        };

        lineIndex = 0;
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine()!;
            ++lineIndex;
            if (line.Length == 0)
            {
                return builder.Build();
            }

            if (!line.StartsWith('@'))
            {
                throw new FormatException($"Not valid TextFabric metadata line: {line}");
            }

            if (line == "@edgeValues")
            {
                continue;
            }

            var equalIndex = line.IndexOf('=');
            if (equalIndex == -1)
            {
                throw new FormatException($"Not valid TextFabric metadata line: {line}");
            }

            var key = line.AsSpan(1, equalIndex - 1).Trim();
            var value = line.Substring(equalIndex + 1).Trim();
            switch (key)
            {
                case "dataset":
                    builder.Dataset = value;
                    break;
                case "datasetName":
                    builder.DatasetName = value;
                    break;
                case "email":
                    builder.Email = value;
                    break;
                case "author":
                    builder.Author = value;
                    break;
                case "description":
                    builder.Description = value;
                    break;
                case "encoders":
                    builder.Encoders = value;
                    break;
                case "valueType":
                    builder.ValueType = value switch
                    {
                        "str" => DocumentValueType.String,
                        "int" => DocumentValueType.Integer,
                        _ => throw new FormatException($"Not valid TextFabric metadata line: {line}")
                    };
                    break;
                case "version":
                    builder.Version = value;
                    break;
                case "website":
                    builder.Website = value;
                    break;
                case "writtenBy":
                    builder.WrittenBy = value;
                    break;
                case "dateWritten":
                    builder.DateWritten = DateTime.Parse(value);
                    break;
                case "language":
                    builder.Language = value;
                    break;
                case "languageCode":
                    builder.LanguageCode = value;
                    break;
                case "languageEnglish":
                    builder.LanguageEnglish = value;
                    break;
                case "provenance":
                    builder.Provenance = value;
                    break;
                case "coreData":
                    builder.CoreData = value;
                    break;
                case "sectionFeatures":
                    builder.SectionFeatures = value.Split(',').Select(v => v.Trim()).ToArray();
                    break;
                case "sectionTypes":
                    builder.SectionTypes = value.Split(',').Select(v => v.Trim()).ToArray();
                    break;
                default:
                    if (key.StartsWith("fmt:"))
                    {
                        var formatKey = key.Slice(4).ToString();
                        builder.Formats ??= new Dictionary<string, string>();
                        builder.Formats[formatKey] = value;
                        break;
                    }

                    var keyString = key.ToString();
                    Console.WriteLine("Unknown metadata key: " + keyString);
                    builder.UnknownData ??= new Dictionary<string, string>();
                    builder.UnknownData[keyString] = value;
                    break;
            }
        }

        throw new FormatException("No metadata found in the file.");
    }
}