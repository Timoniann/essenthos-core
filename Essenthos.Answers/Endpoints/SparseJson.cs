using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Writes a response with every null, false, empty string and empty list left out, with the options every other
/// response is written with otherwise. For the answers made of words, where most of a word's fields are empty: one
/// "the" of the parallel view took about 420 bytes with them and about 110 without. A field that is absent means what
/// its empty value would: no claim, nothing printed, no list.
/// </summary>
internal sealed class SparseJson
{
    private readonly JsonSerializerOptions _options;

    public SparseJson(IOptions<JsonOptions> json)
    {
        var options = json.Value.SerializerOptions;
        _options = new JsonSerializerOptions(options)
        {
            TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver()).WithAddedModifier(LeaveOutEmpty),
        };
        _options.MakeReadOnly();
    }

    public IResult Ok<T>(T value) => Results.Json(value, (JsonTypeInfo<T>)_options.GetTypeInfo(typeof(T)));

    private static void LeaveOutEmpty(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in type.Properties)
        {
            property.ShouldSerialize = (_, value) => value switch
            {
                null => false,
                false => false,
                string text => text.Length > 0,
                ICollection list => list.Count > 0,
                _ => true,
            };
        }
    }
}
