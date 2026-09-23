using System.Text.Json;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Desk;

/// <summary>A question somebody put to the owner against a piece of work, still open.</summary>
/// <param name="Entity">The work it was asked on, as avioniq names it.</param>
/// <param name="Blocking">Whether the work waits for it.</param>
internal sealed record OpenQuestion(
    string Entity,
    string Title,
    int Number,
    string Text,
    IReadOnlyList<string> Options,
    bool Blocking,
    string? Asked,
    string? By);

/// <summary>Work finished and waiting for the owner to accept it.</summary>
internal sealed record SignOff(string Entity, string Title, string Text, string? Since, string? Project);

/// <param name="Problem">Why the board could not be read, in a sentence; null when it was.</param>
internal sealed record QuestionsResponse(
    bool Available,
    string? Problem,
    IReadOnlyList<OpenQuestion> Questions,
    IReadOnlyList<SignOff> SignOffs);

internal sealed record AnswerRequest(string Entity, int Number, string Answer);

/// <param name="Problem">Why avioniq refused, in its own words; null when the answer was recorded.</param>
internal sealed record AnswerResponse(bool Answered, string? Problem);

/// <summary>
/// What is waiting on the owner on the board: the questions put to him, and the work waiting for his
/// acceptance. Answering is his, and <c>avioniq answer</c> is the verb for it, so the console records
/// it as his. Accepting is not offered: avioniq refuses a sign-off that does not come from a person
/// at a terminal, and a console that went round that guard would be the thing the guard is for.
/// </summary>
internal sealed partial class QuestionBoard(Avioniq avioniq)
{
    /// <summary>The longest answer taken, which is a paragraph; the board is not where an essay goes.</summary>
    public const int LongestAnswer = 4000;

    private const string SignOffKind = "sign-off";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public async Task<QuestionsResponse> Read(CancellationToken cancellationToken)
    {
        var questions = await avioniq.Run(["--json", "questions"], cancellationToken);
        if (!questions.Succeeded)
        {
            return new QuestionsResponse(false, Said(questions), [], []);
        }

        var waiting = await avioniq.Run(["--json", "waiting"], cancellationToken);
        if (!waiting.Succeeded)
        {
            return new QuestionsResponse(false, Said(waiting), [], []);
        }

        var open = (JsonSerializer.Deserialize<List<EntityQuestions>>(questions.Output, Web) ?? [])
            .SelectMany(entity => (entity.Questions ?? [])
                .Where(q => q.Answer is null)
                .Select(q => new OpenQuestion(entity.Id, entity.Title ?? entity.Id, q.N, q.Text ?? string.Empty,
                    q.Options ?? [], q.Blocking ?? false, q.Asked, q.By)))
            .OrderByDescending(q => q.Blocking)
            .ThenBy(q => q.Asked, StringComparer.Ordinal)
            .ToList();

        var signOffs = (JsonSerializer.Deserialize<List<WaitingItem>>(waiting.Output, Web) ?? [])
            .Where(item => item.Kind == SignOffKind)
            .Select(item => new SignOff(item.Id, item.Title ?? item.Id, item.Text ?? string.Empty, item.Since, item.Project))
            .ToList();

        return new QuestionsResponse(true, null, open, signOffs);
    }

    public async Task<AnswerResponse> Answer(AnswerRequest request, CancellationToken cancellationToken)
    {
        var answer = request.Answer?.Trim() ?? string.Empty;
        if (!EntityId().IsMatch(request.Entity ?? string.Empty) || request.Number < 1)
        {
            return new AnswerResponse(false, "That question is not one the board holds. Reload the list and answer it again.");
        }

        if (answer.Length == 0 || answer.Length > LongestAnswer)
        {
            return new AnswerResponse(false, $"An answer is between one and {LongestAnswer} characters.");
        }

        // `--by user`: the owner is the one who pressed the button, and avioniq otherwise takes a
        // process with no terminal for an agent.
        var result = await avioniq.Run(
            ["answer", request.Entity!, request.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                answer, "--by", "user"],
            cancellationToken);
        return result.Succeeded ? new AnswerResponse(true, null) : new AnswerResponse(false, Said(result));
    }

    private static string Said(AvioniqResult result) =>
        (result.Error.Trim().Length > 0 ? result.Error : result.Output).Trim() is { Length: > 0 } text
            ? text
            : $"avioniq stopped with status {result.ExitCode} and said nothing.";

    [GeneratedRegex("^[A-Z]{2,5}-[0-9]{1,6}$")]
    private static partial Regex EntityId();

    private sealed record EntityQuestions(string Id, string? Title, List<QuestionEntry>? Questions);

    private sealed record QuestionEntry(
        int N,
        string? Asked,
        string? By,
        string? Text,
        List<string>? Options,
        bool? Blocking,
        string? Answer);

    private sealed record WaitingItem(string Id, string? Kind, string? Title, string? Text, string? Since, string? Project);
}
