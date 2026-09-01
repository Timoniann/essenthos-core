using System.Text.Json.Serialization;
using Essenthos.Core.Endpoints;

namespace Essenthos.Core;

/// <summary>
/// Every record any endpoint returns is registered here, including its list and array forms. A
/// record that is missing fails at runtime on first request rather than at compile time.
/// </summary>
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
