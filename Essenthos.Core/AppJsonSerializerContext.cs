using System.Text.Json.Serialization;
using Essenthos.Core.Endpoints;

namespace Essenthos.Core;

/// <summary>
/// Every record any endpoint returns is registered here, including its list and array forms. A
/// record that is missing fails at runtime on first request rather than at compile time.
/// </summary>
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(DatasetCountsResponse))]
[JsonSerializable(typeof(VerificationResponse))]
[JsonSerializable(typeof(VerificationReportResponse))]
[JsonSerializable(typeof(ProblemResponse))]
[JsonSerializable(typeof(IList<string>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(CorpusListResponse))]
[JsonSerializable(typeof(CorpusResponse))]
[JsonSerializable(typeof(IList<CorpusResponse>))]
[JsonSerializable(typeof(List<CorpusResponse>))]
[JsonSerializable(typeof(CoverageResponse))]
[JsonSerializable(typeof(BookListResponse))]
[JsonSerializable(typeof(BookResponse))]
[JsonSerializable(typeof(IList<BookResponse>))]
[JsonSerializable(typeof(List<BookResponse>))]
[JsonSerializable(typeof(BookRefResponse))]
[JsonSerializable(typeof(VerseRefResponse))]
[JsonSerializable(typeof(MorphologyResponse))]
[JsonSerializable(typeof(EntityRefResponse))]
[JsonSerializable(typeof(TextWordResponse))]
[JsonSerializable(typeof(IList<TextWordResponse>))]
[JsonSerializable(typeof(List<TextWordResponse>))]
[JsonSerializable(typeof(TextVerseResponse))]
[JsonSerializable(typeof(IList<TextVerseResponse>))]
[JsonSerializable(typeof(List<TextVerseResponse>))]
[JsonSerializable(typeof(ChapterTextResponse))]
[JsonSerializable(typeof(ParallelCellResponse))]
[JsonSerializable(typeof(ParallelVerseResponse))]
[JsonSerializable(typeof(IList<ParallelVerseResponse>))]
[JsonSerializable(typeof(List<ParallelVerseResponse>))]
[JsonSerializable(typeof(ParallelTextResponse))]
[JsonSerializable(typeof(Dictionary<string, ParallelCellResponse?>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
