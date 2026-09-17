using Essenthos.Core.Bhsa;

namespace Essenthos.Core.TextFabric;

public class Project
{
    private Project(Dictionary<string, IDocument> documents, IReadOnlyDictionary<string, RangeInt> nodeTypeRanges,
        IReadOnlyDictionary<int, IReadOnlyList<RangeInt>> objectSlotsMap)
    {
        Documents = documents;
        NodeTypeRanges = nodeTypeRanges;
        SlotsDocument = documents["oslots"];
        NodeTypesDocument = documents["otype"];
        ObjectSlotsMap = objectSlotsMap;
    }

    public IReadOnlyDictionary<string, IDocument> Documents { get; }

    public IDocument SlotsDocument { get; }

    public IDocument NodeTypesDocument { get; }

    public IReadOnlyDictionary<string, RangeInt> NodeTypeRanges { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<RangeInt>> ObjectSlotsMap { get; }

    public static Project Load(string path)
    {
        var parser = new DocumentParser();
        var documents = new Dictionary<string, IDocument>();
        var startTime = DateTime.Now;
        var actions = from filePath in Directory.EnumerateFiles(path)
            where filePath.EndsWith(".tf")
            where !filePath.Contains("omap@")
            select (Action)(() =>
            {
                var document = parser.Parse(filePath);
                lock (documents)
                {
                    documents[document.Name] = document;
                }
            });

        Parallel.Invoke(actions.ToArray());

        var endTime = DateTime.Now;
        Console.WriteLine($"Loaded {documents.Count} documents in {endTime - startTime}.");

        if (documents.GetValueOrDefault("oslots") is not IDocument<string> slotsDocument)
        {
            throw new InvalidOperationException("The oslots document is missing.");
        }

        if (documents.GetValueOrDefault("otype") is not Document<string> nodeTypesDocument)
        {
            throw new InvalidOperationException("The otype document is missing.");
        }

        Dictionary<int, IReadOnlyList<RangeInt>> objectSlotsMap = new(slotsDocument.Count);
        foreach (var (key, value) in slotsDocument)
        {
            List<RangeInt> slotRanges = [];
            if (value.Contains(','))
            {
                var parts = value.Split(',');
                foreach (var part in parts)
                {
                    var trimmedPart = part.Trim();
                    if (trimmedPart.Contains('-'))
                    {
                        var rangeParts = trimmedPart.Split('-');
                        if (rangeParts.Length != 2 || !int.TryParse(rangeParts[0], out var start) ||
                            !int.TryParse(rangeParts[1], out var end) || start > end)
                        {
                            throw new FormatException($"Invalid range format in oslots: {trimmedPart}");
                        }

                        slotRanges.Add(new RangeInt(start, end));
                    }
                    else
                    {
                        if (!int.TryParse(trimmedPart, out var singleValue))
                        {
                            throw new FormatException($"Invalid single value in oslots: {trimmedPart}");
                        }

                        slotRanges.Add(new RangeInt(singleValue, singleValue));
                    }
                }
            }
            else
            {
                if (value.Contains('-'))
                {
                    var parts = value.Split('-');
                    if (parts.Length != 2 || !int.TryParse(parts[0], out var start) ||
                        !int.TryParse(parts[1], out var end) || start > end)
                    {
                        throw new FormatException($"Invalid range format in oslots: {value}");
                    }

                    slotRanges.Add(new RangeInt(start, end));
                }
                else
                {
                    if (!int.TryParse(value, out var singleValue))
                    {
                        throw new FormatException($"Invalid single value in oslots: {value}");
                    }

                    slotRanges.Add(new RangeInt(singleValue, singleValue));
                }
            }

            objectSlotsMap[key] = slotRanges;
        }

        var nodeTypes = nodeTypesDocument.Values;
        var nodeTypeRanges = new Dictionary<string, RangeInt>(nodeTypes.Length);

        foreach (var nodeType in nodeTypes)
        {
            nodeTypeRanges[nodeType] = nodeTypesDocument.GetValueRange(nodeType) ??
                                       throw new InvalidOperationException($"Node type '{nodeType}' not found.");
        }

        return new Project(documents, nodeTypeRanges, objectSlotsMap);
    }
}