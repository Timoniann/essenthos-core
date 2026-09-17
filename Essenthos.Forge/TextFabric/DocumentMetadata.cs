namespace Essenthos.Core.TextFabric;

public class DocumentMetadata
{
    private DocumentMetadata()
    {
    }

    public required DocumentType DocumentType { get; init; }

    public required DocumentValueType ValueType { get; init; }

    public string? Author { get; init; }

    public string? Dataset { get; init; }

    public string? DatasetName { get; init; }

    public string? Description { get; init; }

    public string? Email { get; init; }

    public string? Encoders { get; init; }

    public string? Provenance { get; init; }

    public string? Version { get; init; }

    public string? Website { get; init; }

    public string? WrittenBy { get; init; }

    public DateTime? DateWritten { get; init; }

    public string? Language { get; init; }

    public string? LanguageCode { get; init; }

    public string? LanguageEnglish { get; init; }

    public IReadOnlyDictionary<string, string>? Formats { get; init; }

    public IReadOnlyList<string>? SectionFeatures { get; init; }

    public IReadOnlyList<string>? SectionTypes { get; init; }

    public IReadOnlyDictionary<string, string>? UnknownData { get; init; }

    internal class Builder
    {
        public required DocumentType DocumentType { get; set; }
        public required DocumentValueType ValueType { get; set; }
        public string? Author { get; set; }
        public string? Dataset { get; set; }
        public string? DatasetName { get; set; }
        public string? Description { get; set; }
        public string? Email { get; set; }
        public string? Encoders { get; set; }
        public string? Provenance { get; set; }
        public string? Version { get; set; }
        public string? Website { get; set; }
        public string? WrittenBy { get; set; }
        public DateTime? DateWritten { get; set; }
        public string? Language { get; set; }
        public string? LanguageCode { get; set; }
        public string? LanguageEnglish { get; set; }
        public string? CoreData { get; set; }
        public Dictionary<string, string>? Formats { get; set; }
        public IReadOnlyList<string>? SectionFeatures { get; set; }
        public IReadOnlyList<string>? SectionTypes { get; set; }
        public Dictionary<string, string>? UnknownData { get; set; }

        public DocumentMetadata Build() => new()
        {
            DocumentType = DocumentType,
            ValueType = ValueType,
            Author = Author,
            Dataset = Dataset,
            DatasetName = DatasetName,
            Description = Description,
            Email = Email,
            Encoders = Encoders,
            Provenance = Provenance,
            Version = Version,
            Website = Website,
            WrittenBy = WrittenBy,
            DateWritten = DateWritten,
            Language = Language,
            LanguageCode = LanguageCode,
            LanguageEnglish = LanguageEnglish,
            Formats = Formats,
            SectionFeatures = SectionFeatures,
            SectionTypes = SectionTypes,
            UnknownData = UnknownData
        };
    }
}