using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// Books an edition numbers in another tradition than the rest of it, or in none the versification
/// data describes (<see cref="Versification.Unknown"/>, where they stand at their own numbers), as
/// <c>Resources/Essenthos/versification/book-traditions.tsv</c> writes them with what was read for each.
/// </summary>
internal static class BookTraditions
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, Versification>> Editions = Read();

    /// <summary>
    /// The books of this edition numbered in a tradition of their own, or in none the data describes;
    /// empty for most.
    /// </summary>
    public static IReadOnlyDictionary<int, Versification> For(string slug) =>
        Editions.GetValueOrDefault(slug) ?? new Dictionary<int, Versification>();

    private static Dictionary<string, IReadOnlyDictionary<int, Versification>> Read()
    {
        var editions = new Dictionary<string, Dictionary<int, Versification>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in VersificationFiles.Read(VersificationFiles.BookTraditions, "text", "book", "tradition", "why"))
        {
            var code = line.Required("book");
            if (!BookCodes.TryGetOrdinal(code, out var book))
            {
                throw line.Fail($"\"{code}\" is not a book code of the versification data");
            }

            var text = line.Required("text");
            if (!editions.TryGetValue(text, out var books))
            {
                editions[text] = books = [];
            }

            if (!books.TryAdd(book, line.Tradition("tradition")))
            {
                throw line.Fail($"{text} {code} is listed twice");
            }
        }

        return editions.ToDictionary(
            edition => edition.Key,
            edition => (IReadOnlyDictionary<int, Versification>)edition.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
