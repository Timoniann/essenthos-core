using System.Reflection;
using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// Two records the owner has ruled are not one person, so that neither page offers the other however
/// alike they look: Hobab is not Reuel, whom the text also calls Jethro.
/// </summary>
internal sealed record HeldApartRecords(string Policy, IReadOnlyList<HeldApartPair> Pairs)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.HeldApartRecords.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>The list the Forge carries embedded, which <c>records-export</c> writes as <c>records/held-apart</c>.</summary>
    public static HeldApartRecords Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new FileNotFoundException(
                               $"The embedded resource \"{Resource}\" is not in this assembly. It is added by the EmbeddedResource "
                               + "item in Essenthos.Forge.csproj; if the file was moved or renamed, that item and this name move with it.",
                               Resource);
        return JsonSerializer.Deserialize<HeldApartRecords>(stream, Shape)
               ?? throw new InvalidDataException($"The embedded resource \"{Resource}\" is empty.");
    }
}

/// <param name="DecidedBy">Who ruled it, and when.</param>
internal sealed record HeldApartPair(string One, string Other, string Why, string DecidedBy)
{
    public bool Joins(string a, string b) => (One == a && Other == b) || (One == b && Other == a);
}
