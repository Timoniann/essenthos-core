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
}
