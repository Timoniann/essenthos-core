using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Accounts;
using Essenthos.Core.Corpus;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// A reader's suggestions and ideas, and the conversation on each.
///
///     GET  /v1/me/suggestions                 every suggestion of theirs, the latest activity first
///     GET  /v1/me/suggestions/summary         how many have a reply they have not read; for an admin, what waits
///     POST /v1/me/suggestions                 a new one, in one of the categories
///     GET  /v1/me/suggestions/{id}            one, with the admins' replies — never their internal notes
///     POST /v1/me/suggestions/{id}/messages   a follow-up
///
/// Signing in is required to write one: so an answer can reach the person who asked, and so the form
/// is not an open door. Each account is held to <see cref="Limits.SuggestionsPerDay"/> suggestions and
/// <see cref="Limits.SuggestionMessagesPerDay"/> follow-ups a day.
/// </summary>
internal static class SuggestionEndpoints
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private const int ExcerptLength = 240;

    public static void MapSuggestions(this IEndpointRouteBuilder routes)
    {
        var mine = routes.MapGroup("/me/suggestions").RequireAuthorization();

        mine.MapGet("", async (HttpContext context, AccountsDbContext db) =>
        {
            var account = context.User.AccountId();
            var found = await db.Suggestions.AsNoTracking()
                .Where(s => s.AccountId == account)
                .OrderByDescending(s => s.LastActivityAt)
                .ToListAsync(context.RequestAborted);
            return Results.Ok(new SuggestionsResponse(found.Select(Summarise).ToList()));
        });

        mine.MapGet("/summary", async (HttpContext context, AccountsDbContext db, Admins admins) =>
        {
            var account = context.User.AccountId();
            var unread = await db.Suggestions.CountAsync(
                s => s.AccountId == account && s.LastReplyAt != null && s.LastReplyAt > s.AuthorSeenAt, context.RequestAborted);
            var waiting = await admins.IsAdmin(db, account, context.RequestAborted)
                ? await AdminEndpoints.Counts(db, account, context.RequestAborted)
                : null;
            return Results.Ok(new SuggestionsSummaryResponse(unread, waiting));
        });

        mine.MapPost("", async (HttpContext context, AccountsDbContext db, SuggestionCreate create) =>
        {
            var (form, problem) = Check(create);
            if (form is null)
            {
                return Results.BadRequest(new ProblemResponse(problem!));
            }

            var account = context.User.AccountId();
            var now = DateTimeOffset.UtcNow;
            var today = await db.Suggestions.CountAsync(s => s.AccountId == account && s.CreatedAt > now - Day, context.RequestAborted);
            if (today >= Limits.SuggestionsPerDay)
            {
                return TooMany($"An account sends at most {Limits.SuggestionsPerDay} suggestions a day.");
            }

            var suggestion = new Suggestion
            {
                Id = Guid.CreateVersion7(),
                AccountId = account,
                Category = form.Category,
                Status = Suggestion.Statuses[0],
                Body = form.Body,
                Fields = JsonSerializer.Serialize(form.Fields, AppJsonSerializerContext.Default.DictionaryStringString),
                Links = form.Links,
                Language = form.Fields.GetValueOrDefault("language"),
                HasLink = HasLink(form),
                Locale = Locale(create.Locale),
                Search = Searchable(form),
                CreatedAt = now,
                UpdatedAt = now,
                LastActivityAt = now,
                AuthorSeenAt = now,
            };
            db.Suggestions.Add(suggestion);
            await db.SaveChangesAsync(context.RequestAborted);
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("suggestions")
                .LogInformation("A suggestion in {Category}", suggestion.Category);
            return Results.Ok(await Describe(db, suggestion, account, includeNotes: false, context.RequestAborted));
        });

        mine.MapGet("/{id:guid}", async (Guid id, HttpContext context, AccountsDbContext db) =>
        {
            var account = context.User.AccountId();
            if (await db.Suggestions.FirstOrDefaultAsync(s => s.Id == id && s.AccountId == account, context.RequestAborted) is not { } suggestion)
            {
                return Results.NotFound(new ProblemResponse("There is no such suggestion."));
            }

            suggestion.AuthorSeenAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(await Describe(db, suggestion, account, includeNotes: false, context.RequestAborted));
        });

        mine.MapPost("/{id:guid}/messages", async (Guid id, HttpContext context, AccountsDbContext db, SuggestionMessageCreate create) =>
        {
            var account = context.User.AccountId();
            if (await db.Suggestions.FirstOrDefaultAsync(s => s.Id == id && s.AccountId == account, context.RequestAborted) is not { } suggestion)
            {
                return Results.NotFound(new ProblemResponse("There is no such suggestion."));
            }

            if (Message(create.Body) is not { } body)
            {
                return Results.BadRequest(new ProblemResponse(MessageRule));
            }

            var now = DateTimeOffset.UtcNow;
            var today = await db.SuggestionMessages.CountAsync(
                m => m.AuthorId == account && m.Kind == SuggestionMessage.Message && m.At > now - Day, context.RequestAborted);
            if (today >= Limits.SuggestionMessagesPerDay)
            {
                return TooMany($"An account writes at most {Limits.SuggestionMessagesPerDay} messages a day.");
            }

            db.SuggestionMessages.Add(new SuggestionMessage
            {
                SuggestionId = suggestion.Id,
                AuthorId = account,
                Kind = SuggestionMessage.Message,
                Body = body,
                At = now,
            });
            suggestion.LastActivityAt = now;
            suggestion.UpdatedAt = now;
            suggestion.AuthorSeenAt = now;
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(await Describe(db, suggestion, account, includeNotes: false, context.RequestAborted));
        });
    }

    private static IResult TooMany(string message) =>
        Results.Json(new ProblemResponse(message), statusCode: StatusCodes.Status429TooManyRequests);

    internal const string MessageRule = "A message is 1 to 10,000 characters.";

    /// <summary>The message trimmed, its line endings made one kind, or null for an empty or too long one.</summary>
    internal static string? Message(string? body) =>
        Clean(body) is { Length: > 0 and <= Limits.SuggestionMessage } cleaned ? cleaned : null;

    internal static SuggestionSummaryResponse Summarise(Suggestion suggestion) => new(
        suggestion.Id,
        suggestion.Category,
        suggestion.Status,
        Excerpt(suggestion.Body),
        Fields(suggestion.Fields),
        suggestion.HasLink,
        suggestion.CreatedAt,
        suggestion.LastActivityAt,
        suggestion.LastReplyAt is { } reply && reply > suggestion.AuthorSeenAt);

    internal static string? Excerpt(string? body) =>
        body is null ? null : body.Length <= ExcerptLength ? body : body[..ExcerptLength].TrimEnd() + "…";

    internal static Dictionary<string, string> Fields(string json) =>
        JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.DictionaryStringString) ?? [];

    /// <summary>
    /// One suggestion with its thread. The author is shown the admins' replies and never their notes —
    /// filtered here rather than by the client, which is the only place a filter holds.
    /// </summary>
    internal static async Task<SuggestionResponse> Describe(
        AccountsDbContext db, Suggestion suggestion, Guid viewer, bool includeNotes, CancellationToken cancellationToken)
    {
        var messages = await db.SuggestionMessages.AsNoTracking()
            .Where(m => m.SuggestionId == suggestion.Id && (includeNotes || m.Kind != SuggestionMessage.Note))
            .OrderBy(m => m.At).ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id, m.Kind, m.Body, m.At, m.AuthorId,
                Name = db.Accounts.Where(a => a.Id == m.AuthorId).Select(a => a.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new SuggestionResponse(
            suggestion.Id,
            suggestion.Category,
            suggestion.Status,
            suggestion.Body,
            Fields(suggestion.Fields),
            suggestion.Links,
            suggestion.CreatedAt,
            suggestion.LastActivityAt,
            messages.Select(m => new SuggestionMessageResponse(m.Id, m.Kind, m.Body, m.At, m.Name, m.AuthorId == viewer)).ToList());
    }

    /// <summary>What a valid request comes to: the category, the main text, the fields it asks for, and its links.</summary>
    internal sealed record Form(string Category, string? Body, Dictionary<string, string> Fields, List<string> Links);

    private sealed record FieldRule(string Key, int Max);

    private sealed record Category(bool BodyRequired, bool TakesLinks, string? Required, FieldRule[] Fields);

    private const int Short = Limits.SuggestionField;
    private const int Long = Limits.SuggestionLongField;

    /// <summary>
    /// What each category asks for. Everything but the category's one required field is optional: a
    /// reader who knows only the name of a translation should still be able to ask for it.
    /// </summary>
    private static readonly Dictionary<string, Category> Categories = new()
    {
        ["text"] = new(false, true, "title",
            [new("language", Short), new("title", Short), new("translator", Short), new("year", 20), new("licence", Short)]),
        ["error"] = new(true, false, null,
            [new("book", 32), new("chapter", 4), new("verse", 4), new("text", 32), new("fix", Long)]),
        ["record"] = new(true, true, null,
            [new("recordKind", 16), new("record", Short), new("recordName", Short), new("sources", Long)]),
        ["feature"] = new(true, false, null, [new("where", Short), new("why", Long)]),
        ["picture"] = new(true, true, null,
            [new("recordKind", 16), new("record", Short), new("recordName", Short), new("sources", Long)]),
        ["other"] = new(true, false, null, []),
    };

    /// <summary>The kinds of record the encyclopedia addresses, as its routes spell them.</summary>
    private static readonly string[] RecordKinds = ["person", "place", "people", "term", "title", "object", "observance"];

    private static readonly Regex Slug = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TextSlug = new("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] Locales = ["en", "uk", "de", "es"];

    /// <summary>
    /// The request as a <see cref="Form"/>, or what is wrong with it. Every value is trimmed, an empty
    /// one is left out, and a key the category does not ask for is refused rather than stored — the
    /// fields are read by a person later, and something nobody asked for is something nobody reads.
    /// </summary>
    internal static (Form? Form, string? Problem) Check(SuggestionCreate create)
    {
        if (create.Category is not { } name || !Categories.TryGetValue(name, out var category))
        {
            return (null, $"A suggestion is one of: {string.Join(", ", Categories.Keys)}.");
        }

        var body = Clean(create.Body);
        if (body is { Length: > Limits.SuggestionBody })
        {
            return (null, $"The text is at most {Limits.SuggestionBody:N0} characters.");
        }

        if (category.BodyRequired && body is null)
        {
            return (null, "Say what the suggestion is: the main text is required.");
        }

        var fields = new Dictionary<string, string>();
        foreach (var (key, value) in create.Fields ?? [])
        {
            if (category.Fields.FirstOrDefault(f => f.Key == key) is not { } rule)
            {
                return (null, $"\"{key}\" is not asked for in this category.");
            }

            if (Clean(value) is not { } cleaned)
            {
                continue;
            }

            if (cleaned.Length > rule.Max)
            {
                return (null, $"\"{key}\" is at most {rule.Max:N0} characters.");
            }

            fields[key] = cleaned;
        }

        if (category.Required is { } required && !fields.ContainsKey(required))
        {
            return (null, $"\"{required}\" is required in this category.");
        }

        if (Place(fields) is { } placeProblem)
        {
            return (null, placeProblem);
        }

        if (Record(fields) is { } recordProblem)
        {
            return (null, recordProblem);
        }

        var links = new List<string>();
        foreach (var link in create.Links ?? [])
        {
            if (Clean(link) is not { } cleaned)
            {
                continue;
            }

            if (!category.TakesLinks)
            {
                return (null, "This category does not take links; put them in the text.");
            }

            if (!IsLink(cleaned))
            {
                return (null, $"\"{Shorten(cleaned)}\" is not a web address: a link starts with https:// or http://.");
            }

            if (!links.Contains(cleaned))
            {
                links.Add(cleaned);
            }
        }

        if (links.Count > Limits.SuggestionLinks)
        {
            return (null, $"A suggestion carries at most {Limits.SuggestionLinks} links.");
        }

        return (new Form(name, body, fields, links), null);
    }

    /// <summary>
    /// A verse named by an error report: a book of the canon, spelt as the reader's address spells it,
    /// and a chapter and a verse that are numbers. Whether the verse exists is not asked — a verse a
    /// text is missing is one of the errors worth reporting.
    /// </summary>
    private static string? Place(Dictionary<string, string> fields)
    {
        if (fields.TryGetValue("book", out var book))
        {
            if (BookReferences.ResolveOrdinal(book) is not { } ordinal)
            {
                return $"\"{book}\" is not a book.";
            }

            fields["book"] = BookReferences.Slug(ordinal);
        }

        foreach (var key in new[] { "chapter", "verse" })
        {
            if (fields.TryGetValue(key, out var number) && !(int.TryParse(number, out var parsed) && parsed > 0))
            {
                return $"The {key} is a whole number from 1.";
            }
        }

        if ((fields.ContainsKey("chapter") && !fields.ContainsKey("book")) ||
            (fields.ContainsKey("verse") && !fields.ContainsKey("chapter")))
        {
            return "A verse is named with its chapter, and a chapter with its book.";
        }

        return fields.TryGetValue("text", out var text) && !TextSlug.IsMatch(text)
            ? $"\"{text}\" is not a text's abbreviation."
            : null;
    }

    private static string? Record(Dictionary<string, string> fields)
    {
        var hasKind = fields.TryGetValue("recordKind", out var kind);
        var hasRecord = fields.TryGetValue("record", out var record);
        if (hasKind != hasRecord)
        {
            return "A record is named by its kind and its address together.";
        }

        if (hasKind && !RecordKinds.Contains(kind))
        {
            return $"A record is one of: {string.Join(", ", RecordKinds)}.";
        }

        return hasRecord && !Slug.IsMatch(record!) ? $"\"{record}\" is not a record's address." : null;
    }

    /// <summary>An absolute http or https address with a host, and nothing a browser would read otherwise.</summary>
    internal static bool IsLink(string link) =>
        link.Length <= Limits.SuggestionLink &&
        !link.Any(char.IsWhiteSpace) &&
        Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        uri.Host.Length > 0;

    internal static bool HasLink(Form form) =>
        form.Links.Count > 0 ||
        (form.Body?.Contains("http://", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (form.Body?.Contains("https://", StringComparison.OrdinalIgnoreCase) ?? false);

    internal static string Searchable(Form form) =>
        string.Join('\n', new[] { form.Body }.Concat(form.Fields.Values).Concat(form.Links).OfType<string>()).ToLowerInvariant();

    private static string? Locale(string? locale) => locale is not null && Locales.Contains(locale) ? locale : null;

    /// <summary>Trimmed, with one kind of line ending, and null when nothing is left.</summary>
    internal static string? Clean(string? value) =>
        value?.Replace("\r\n", "\n").Replace('\r', '\n').Trim() is { Length: > 0 } cleaned ? cleaned : null;

    private static string Shorten(string value) => value.Length <= 60 ? value : value[..60] + "…";
}

/// <param name="Fields">The category's own fields, by key; a value left empty is left out.</param>
/// <param name="Locale">The interface language the reader is writing in.</param>
internal record SuggestionCreate(
    string? Category,
    string? Body,
    Dictionary<string, string?>? Fields,
    IReadOnlyList<string>? Links,
    string? Locale);

internal record SuggestionMessageCreate(string? Body);

/// <param name="Excerpt">The start of the main text, enough for a list.</param>
/// <param name="Unread">An admin has replied since its author last opened it.</param>
internal record SuggestionSummaryResponse(
    Guid Id,
    string Category,
    string Status,
    string? Excerpt,
    Dictionary<string, string> Fields,
    bool HasLink,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    bool Unread);

internal record SuggestionsResponse(IReadOnlyList<SuggestionSummaryResponse> Items);

/// <param name="Kind"><c>message</c> from the author, <c>reply</c> from an admin, <c>note</c> between admins.</param>
/// <param name="AuthorName">Null once the writer's account is gone.</param>
/// <param name="Mine">Written by whoever is asking.</param>
internal record SuggestionMessageResponse(long Id, string Kind, string Body, DateTimeOffset At, string? AuthorName, bool Mine);

internal record SuggestionResponse(
    Guid Id,
    string Category,
    string Status,
    string? Body,
    Dictionary<string, string> Fields,
    IReadOnlyList<string> Links,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    IReadOnlyList<SuggestionMessageResponse> Messages);

/// <param name="Unread">The reader's suggestions with a reply they have not read.</param>
/// <param name="Admin">For an admin, what waits in the admin area; null for everyone else.</param>
internal record SuggestionsSummaryResponse(int Unread, AdminCountsResponse? Admin);
