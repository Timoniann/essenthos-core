namespace Essenthos.Core.Endpoints;

internal static class ApiResults
{
    /// <summary>
    /// A malformed reference answers 400 with a plain-text hint naming the expected form
    ///. <c>Results.BadRequest(string)</c> would send it as a JSON string,
    /// quotes and all, which a client then has to parse to read an error message.
    /// </summary>
    public static IResult Malformed(string hint)
    {
        return Results.Text(hint, "text/plain", statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Says what is absent and, where the two differ, which kind of absence it is: a text that does
    /// not contain a book is a different fact from a book with no text in it, and a reader told the
    /// first will stop looking for the second.
    /// </summary>
    public static IResult NotFound(string what)
    {
        return Results.Text(what, "text/plain", statusCode: StatusCodes.Status404NotFound);
    }
}
