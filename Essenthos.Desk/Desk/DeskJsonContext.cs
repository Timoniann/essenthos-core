using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Essenthos.Core.Desk;

/// <summary>Every shape the console's endpoints take and return, compiled rather than reflected.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(SummaryResponse))]
[JsonSerializable(typeof(ProblemResponse))]
[JsonSerializable(typeof(ChangeLogResponse))]
[JsonSerializable(typeof(SiteSwitchesResponse))]
[JsonSerializable(typeof(SiteSwitch))]
[JsonSerializable(typeof(SiteSwitchRequest))]
[JsonSerializable(typeof(BriefFieldRequest))]
[JsonSerializable(typeof(PortraitStatusRequest))]
[JsonSerializable(typeof(PortraitReviewRequest))]
[JsonSerializable(typeof(PicturedResponse))]
[JsonSerializable(typeof(PictureSet))]
[JsonSerializable(typeof(PictureChoiceRequest))]
[JsonSerializable(typeof(RelationshipDecisionRequest))]
[JsonSerializable(typeof(RelationshipBulkRequest))]
[JsonSerializable(typeof(RelationshipDecisionResponse))]
[JsonSerializable(typeof(ThingAnswerRequest))]
[JsonSerializable(typeof(ThingRecordRequest))]
[JsonSerializable(typeof(ThingQuestion))]
[JsonSerializable(typeof(ThingQuestionsResponse))]
[JsonSerializable(typeof(ThingRecordEntry))]
[JsonSerializable(typeof(ThingRecordsResponse))]
[JsonSerializable(typeof(PortraitsResponse))]
[JsonSerializable(typeof(PortraitDetail))]
[JsonSerializable(typeof(OperationsResponse))]
[JsonSerializable(typeof(RunStarted))]
[JsonSerializable(typeof(RunLog))]
internal sealed partial class DeskJsonContext : JsonSerializerContext;
