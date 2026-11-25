using System.Text.Json;
using System.Text.Json.Serialization;
using Essenthos.Core.Endpoints;

namespace Essenthos.Core;

/// <summary>
/// The answers composed here and written without the API's own serializer: the ones a page carries
/// (<see cref="Pages.PageData"/>) and the few read or written inside a handler. Generated for the same web
/// defaults as the API's context, so an answer comes out byte for byte as the API writes it. The API's
/// context lists these types too, with every other answer it returns.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(EntityResponse))]
[JsonSerializable(typeof(EventResponse))]
[JsonSerializable(typeof(PeriodResponse))]
[JsonSerializable(typeof(StrongEntryResponse))]
[JsonSerializable(typeof(StrongRenderingsResponse))]
[JsonSerializable(typeof(StrongBookMapResponse))]
[JsonSerializable(typeof(OccupationDetailResponse))]
[JsonSerializable(typeof(TextNumberingResponse))]
[JsonSerializable(typeof(System.Text.Json.Nodes.JsonObject))]
[JsonSerializable(typeof(List<PlaceOutlineBand>))]
internal partial class AnswerJsonContext : JsonSerializerContext;
