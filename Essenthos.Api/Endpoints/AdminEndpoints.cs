using Essenthos.Core.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The admin area: readers' suggestions, answered and triaged, and the accounts that may do it.
///
///     GET   /v1/admin/summary                     what waits: new, in review, given to me
///     POST  /v1/admin/suggestions/search          filtered by category, status, language, date, link, who has it; searched
///     GET   /v1/admin/suggestions/{id}            everything asked, the whole thread with the notes, its history
///     POST  /v1/admin/suggestions/{id}/messages   a reply the reader sees, or a note they never do
///     PATCH /v1/admin/suggestions/{id}            its status, and who has it
///     GET   /v1/admin/admins                      who a suggestion can be given to
///     POST  /v1/admin/users/search                accounts, searched by name or address
///     PUT   /v1/admin/users/{id}/admin            make an account an admin, or stop it being one
///     GET   /v1/admin/actions                     everything admins did, the latest first
///
/// Every route checks, on every request, that the account asking is an admin — the client hiding a
/// link is a convenience, never the check. Every change is written to <see cref="AdminAction"/> in the
/// same save as the change itself.
/// </summary>
internal static class AdminEndpoints
{
    private const int PageSize = 50;

    public static void MapAdmin(this IEndpointRouteBuilder routes)
    {
        var admin = routes.MapGroup("/admin").RequireAuthorization().AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var db = http.RequestServices.GetRequiredService<AccountsDbContext>();
            var admins = http.RequestServices.GetRequiredService<Admins>();
            return await admins.IsAdmin(db, http.User.AccountId(), http.RequestAborted)
                ? await next(context)
                : Results.Json(new ProblemResponse("This is for the site's admins."), statusCode: StatusCodes.Status403Forbidden);
        });

        admin.MapGet("/summary", async (HttpContext context, AccountsDbContext db) =>
            Results.Ok(await Counts(db, context.User.AccountId(), context.RequestAborted)));

        // A search is posted rather than put in the address: what an admin searches for may be a
        // reader's name or address, and an address bar, a proxy's log and a browser's history keep
        // whatever a URL carries.
        admin.MapPost("/suggestions/search", async (HttpContext context, AccountsDbContext db, AdminSuggestionQuery search) =>
        {
            var (category, status, language, from, to, link, assigned, author, q, skip) = search;
            var me = context.User.AccountId();
            var query = db.Suggestions.AsNoTracking();

            if (category is not null)
            {
                query = query.Where(s => s.Category == category);
            }

            if (author is { } writer)
            {
                query = query.Where(s => s.AccountId == writer);
            }

            if (language is not null)
            {
                query = query.Where(s => s.Language == language);
            }

            if (from is { } first)
            {
                var start = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                query = query.Where(s => s.CreatedAt >= start);
            }

            if (to is { } last)
            {
                var end = new DateTimeOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                query = query.Where(s => s.CreatedAt < end);
            }

            if (link is { } hasLink)
            {
                query = query.Where(s => s.HasLink == hasLink);
            }

            query = assigned switch
            {
                "me" => query.Where(s => s.AssignedTo == me),
                "none" => query.Where(s => s.AssignedTo == null),
                _ => query,
            };

            if (q?.Trim() is { Length: > 0 } words)
            {
                var pattern = $"%{Like(words.ToLowerInvariant())}%";
                query = query.Where(s => EF.Functions.Like(s.Search, pattern) ||
                    db.Accounts.Any(a => a.Id == s.AccountId && EF.Functions.ILike(a.DisplayName, pattern)));
            }

            // Counted before the status narrows it, so the tabs say how many each status holds under
            // the other filters.
            var counts = await query.GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Status, g => g.Count, context.RequestAborted);

            if (status is not null)
            {
                query = query.Where(s => s.Status == status);
            }

            var total = await query.CountAsync(context.RequestAborted);
            var page = await query
                .OrderByDescending(s => s.LastActivityAt)
                .Skip(Math.Max(skip ?? 0, 0))
                .Take(PageSize)
                .Select(s => new
                {
                    Suggestion = s,
                    Author = db.Accounts.Where(a => a.Id == s.AccountId).Select(a => a.DisplayName).FirstOrDefault(),
                    Assigned = db.Accounts.Where(a => a.Id == s.AssignedTo).Select(a => a.DisplayName).FirstOrDefault(),
                    Messages = db.SuggestionMessages.Count(m => m.SuggestionId == s.Id && m.Kind != SuggestionMessage.Note),
                })
                .ToListAsync(context.RequestAborted);

            var languages = await db.Suggestions.AsNoTracking()
                .Where(s => s.Language != null)
                .Select(s => s.Language!)
                .Distinct()
                .OrderBy(l => l)
                .ToListAsync(context.RequestAborted);

            return Results.Ok(new AdminSuggestionsResponse(
                page.Select(row => new AdminSuggestionSummaryResponse(
                    SuggestionEndpoints.Summarise(row.Suggestion) with { Unread = false },
                    row.Suggestion.Language,
                    row.Author,
                    row.Assigned,
                    row.Messages)).ToList(),
                total,
                counts,
                languages));
        });

        admin.MapGet("/suggestions/{id:guid}", async (Guid id, HttpContext context, AccountsDbContext db, Admins admins) =>
            await db.Suggestions.FirstOrDefaultAsync(s => s.Id == id, context.RequestAborted) is { } suggestion
                ? Results.Ok(await Describe(db, admins, suggestion, context.User.AccountId(), context.RequestAborted))
                : NoSuchSuggestion());

        admin.MapPost("/suggestions/{id:guid}/messages", async (Guid id, HttpContext context, AccountsDbContext db, Admins admins, AdminMessageCreate create) =>
        {
            if (await db.Suggestions.FirstOrDefaultAsync(s => s.Id == id, context.RequestAborted) is not { } suggestion)
            {
                return NoSuchSuggestion();
            }

            if (SuggestionEndpoints.Message(create.Body) is not { } body)
            {
                return Results.BadRequest(new ProblemResponse(SuggestionEndpoints.MessageRule));
            }

            var me = context.User.AccountId();
            var actor = await Name(db, me, context.RequestAborted);
            var now = DateTimeOffset.UtcNow;
            var kind = create.Internal ? SuggestionMessage.Note : SuggestionMessage.Reply;
            db.SuggestionMessages.Add(new SuggestionMessage { SuggestionId = id, AuthorId = me, Kind = kind, Body = body, At = now });
            db.AdminActions.Add(Action(actor, me, create.Internal ? AdminAction.Noted : AdminAction.Replied, now, suggestion: id));

            if (!create.Internal)
            {
                suggestion.LastReplyAt = now;
                suggestion.LastActivityAt = now;

                // Answering a new suggestion is taking it up.
                if (suggestion.Status == Suggestion.Statuses[0])
                {
                    db.AdminActions.Add(Action(actor, me, AdminAction.StatusChanged, now, suggestion: id,
                        before: suggestion.Status, after: "review"));
                    suggestion.Status = "review";
                }
            }

            suggestion.UpdatedAt = now;
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(await Describe(db, admins, suggestion, me, context.RequestAborted));
        });

        admin.MapPatch("/suggestions/{id:guid}", async (Guid id, HttpContext context, AccountsDbContext db, Admins admins, AdminSuggestionUpdate update) =>
        {
            if (await db.Suggestions.FirstOrDefaultAsync(s => s.Id == id, context.RequestAborted) is not { } suggestion)
            {
                return NoSuchSuggestion();
            }

            var me = context.User.AccountId();
            var actor = await Name(db, me, context.RequestAborted);
            var now = DateTimeOffset.UtcNow;

            if (update.Status is { } status && status != suggestion.Status)
            {
                if (!Suggestion.Statuses.Contains(status))
                {
                    return Results.BadRequest(new ProblemResponse($"A status is one of: {string.Join(", ", Suggestion.Statuses)}."));
                }

                db.AdminActions.Add(Action(actor, me, AdminAction.StatusChanged, now, suggestion: id, before: suggestion.Status, after: status));
                suggestion.Status = status;
            }

            // An empty string gives it back to nobody; a missing one leaves it where it is.
            if (update.AssignedTo is { } assignee)
            {
                Guid? target = null;
                if (assignee.Length > 0)
                {
                    if (!Guid.TryParse(assignee, out var parsed) || !await admins.IsAdmin(db, parsed, context.RequestAborted))
                    {
                        return Results.BadRequest(new ProblemResponse("A suggestion is given to one of the admins."));
                    }

                    target = parsed;
                }

                if (target != suggestion.AssignedTo)
                {
                    var before = suggestion.AssignedTo is { } previous ? await Name(db, previous, context.RequestAborted) : null;
                    var after = target is { } next ? await Name(db, next, context.RequestAborted) : null;
                    db.AdminActions.Add(Action(actor, me, AdminAction.Assigned, now, suggestion: id, before: before, after: after));
                    suggestion.AssignedTo = target;
                }
            }

            suggestion.UpdatedAt = now;
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(await Describe(db, admins, suggestion, me, context.RequestAborted));
        });

        admin.MapGet("/admins", async (HttpContext context, AccountsDbContext db, Admins admins) =>
            Results.Ok(new AdminsResponse((await admins.All(db, context.RequestAborted))
                .Select(a => new AdminNameResponse(a.Id, a.Name)).ToList())));

        admin.MapPost("/users/search", async (HttpContext context, AccountsDbContext db, Admins admins, AdminUserQuery search) =>
        {
            var (q, admin, skip) = search;
            var query = db.Accounts.AsNoTracking();
            if (q?.Trim() is { Length: > 0 } words)
            {
                var pattern = $"%{Like(words)}%";
                query = query.Where(a => EF.Functions.ILike(a.DisplayName, pattern) ||
                    db.AccountEmails.Any(e => e.AccountId == a.Id && EF.Functions.ILike(e.Email, pattern)));
            }

            if (admin == true)
            {
                query = query.Where(a => a.Admin ||
                    db.AccountEmails.Any(e => e.AccountId == a.Id && admins.Configured.Contains(e.Email)));
            }

            var total = await query.CountAsync(context.RequestAborted);
            var ids = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip(Math.Max(skip ?? 0, 0))
                .Take(PageSize)
                .Select(a => a.Id)
                .ToListAsync(context.RequestAborted);
            return Results.Ok(new AdminUsersResponse(await Users(db, admins, ids, context.RequestAborted), total));
        });

        admin.MapPut("/users/{id:guid}/admin", async (Guid id, HttpContext context, AccountsDbContext db, Admins admins, AdminGrant grant) =>
        {
            if (await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, context.RequestAborted) is not { } account)
            {
                return Results.NotFound(new ProblemResponse("There is no such account."));
            }

            var me = context.User.AccountId();
            if (!grant.Admin)
            {
                if (id == me)
                {
                    return Results.BadRequest(new ProblemResponse("An admin cannot stop being one themselves; another admin can."));
                }

                if ((await admins.ConfiguredAmong(db, [id], context.RequestAborted)).Count > 0)
                {
                    return Results.BadRequest(new ProblemResponse(
                        $"This admin is named in the site's configuration, under {Admins.ConfigurationKey}; they stop being one when the address is taken out there."));
                }
            }

            if (account.Admin != grant.Admin)
            {
                var now = DateTimeOffset.UtcNow;
                var actor = await Name(db, me, context.RequestAborted);
                db.AdminActions.Add(Action(actor, me, grant.Admin ? AdminAction.GrantAdmin : AdminAction.RevokeAdmin, now,
                    account: id, accountName: account.DisplayName));
                account.Admin = grant.Admin;
                await db.SaveChangesAsync(context.RequestAborted);
            }

            return Results.Ok((await Users(db, admins, [id], context.RequestAborted))[0]);
        });

        admin.MapGet("/actions", async (HttpContext context, AccountsDbContext db, Guid? account, int? skip) =>
        {
            var query = db.AdminActions.AsNoTracking();
            if (account is { } target)
            {
                query = query.Where(a => a.AccountId == target || a.ActorId == target);
            }

            var total = await query.CountAsync(context.RequestAborted);
            var page = await query
                .OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
                .Skip(Math.Max(skip ?? 0, 0))
                .Take(PageSize)
                .ToListAsync(context.RequestAborted);
            return Results.Ok(new AdminActionsResponse(page.Select(Describe).ToList(), total));
        });
    }

    /// <summary>What waits for the admins, and for this one.</summary>
    internal static async Task<AdminCountsResponse> Counts(AccountsDbContext db, Guid admin, CancellationToken cancellationToken)
    {
        var open = await db.Suggestions
            .Where(s => s.Status == "new" || s.Status == "review")
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var mine = await db.Suggestions.CountAsync(
            s => s.AssignedTo == admin && (s.Status == "new" || s.Status == "review"), cancellationToken);
        return new AdminCountsResponse(
            open.FirstOrDefault(g => g.Status == "new")?.Count ?? 0,
            open.FirstOrDefault(g => g.Status == "review")?.Count ?? 0,
            mine);
    }

    private static IResult NoSuchSuggestion() => Results.NotFound(new ProblemResponse("There is no such suggestion."));

    private static async Task<AdminSuggestionResponse> Describe(
        AccountsDbContext db, Admins admins, Suggestion suggestion, Guid viewer, CancellationToken cancellationToken)
    {
        var author = await Users(db, admins, [suggestion.AccountId], cancellationToken);
        var history = await db.AdminActions.AsNoTracking()
            .Where(a => a.SuggestionId == suggestion.Id)
            .OrderBy(a => a.At).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        return new AdminSuggestionResponse(
            await SuggestionEndpoints.Describe(db, suggestion, viewer, includeNotes: true, cancellationToken),
            author.FirstOrDefault(),
            suggestion.Language,
            suggestion.Locale,
            suggestion.AssignedTo,
            history.Select(Describe).ToList());
    }

    private static AdminActionResponse Describe(AdminAction action) => new(
        action.Id,
        action.At, action.ActorName, action.Action, action.SuggestionId, action.AccountId, action.AccountName, action.Before, action.After);

    /// <summary>The accounts named, in the order named, each with the address and role an admin sees.</summary>
    private static async Task<List<AdminUserResponse>> Users(
        AccountsDbContext db, Admins admins, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await db.Accounts.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new
            {
                a.Id, a.DisplayName, a.ProviderPhotoUrl, a.PhotoVersion, a.CreatedAt, a.Admin,
                Email = db.AccountEmails.Where(e => e.AccountId == a.Id).OrderBy(e => e.CreatedAt).Select(e => e.Email).FirstOrDefault(),
                Suggestions = db.Suggestions.Count(s => s.AccountId == a.Id),
            })
            .ToListAsync(cancellationToken);
        var configured = await admins.ConfiguredAmong(db, ids, cancellationToken);
        var order = ids.ToList();
        return rows
            .OrderBy(r => order.IndexOf(r.Id))
            .Select(r => new AdminUserResponse(
                r.Id,
                r.DisplayName,
                r.PhotoVersion > 0 ? $"/v1/accounts/{r.Id}/photo?v={r.PhotoVersion}" : r.ProviderPhotoUrl,
                r.Email,
                r.CreatedAt,
                r.Admin || configured.Contains(r.Id),
                configured.Contains(r.Id),
                r.Suggestions))
            .ToList();
    }

    private static async Task<string> Name(AccountsDbContext db, Guid account, CancellationToken cancellationToken) =>
        await db.Accounts.Where(a => a.Id == account).Select(a => a.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? "";

    internal static AdminAction Action(
        string actorName,
        Guid actor,
        string action,
        DateTimeOffset at,
        Guid? suggestion = null,
        Guid? account = null,
        string? accountName = null,
        string? before = null,
        string? after = null) => new()
    {
        At = at,
        ActorId = actor,
        ActorName = actorName,
        Action = action,
        SuggestionId = suggestion,
        AccountId = account,
        AccountName = accountName,
        Before = before,
        After = after,
    };

    /// <summary>The words of a search, with LIKE's own wildcards made literal.</summary>
    internal static string Like(string words) =>
        words.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

internal record AdminCountsResponse(int New, int Review, int AssignedToMe);

/// <param name="From">The first day, inclusive, in UTC.</param>
/// <param name="To">The last day, inclusive, in UTC.</param>
/// <param name="Assigned"><c>me</c>, <c>none</c>, or null for anybody.</param>
/// <param name="Author">One reader's suggestions only.</param>
internal record AdminSuggestionQuery(
    string? Category,
    string? Status,
    string? Language,
    DateOnly? From,
    DateOnly? To,
    bool? Link,
    string? Assigned,
    Guid? Author,
    string? Q,
    int? Skip);

/// <param name="Admin">True for admins only.</param>
internal record AdminUserQuery(string? Q, bool? Admin, int? Skip);

internal record AdminSuggestionSummaryResponse(
    SuggestionSummaryResponse Suggestion,
    string? Language,
    string? AuthorName,
    string? AssignedName,
    int Messages);

/// <param name="Counts">How many each status holds under the other filters.</param>
/// <param name="Languages">Every language a suggestion names, for the filter.</param>
internal record AdminSuggestionsResponse(
    IReadOnlyList<AdminSuggestionSummaryResponse> Items,
    int Total,
    Dictionary<string, int> Counts,
    IReadOnlyList<string> Languages);

/// <param name="Author">Null once the reader's account is gone — and then the suggestion is gone with it.</param>
/// <param name="Locale">The interface language it was written in.</param>
internal record AdminSuggestionResponse(
    SuggestionResponse Suggestion,
    AdminUserResponse? Author,
    string? Language,
    string? Locale,
    Guid? AssignedTo,
    IReadOnlyList<AdminActionResponse> History);

/// <param name="Internal">A note for the admins, which the reader never sees.</param>
internal record AdminMessageCreate(string? Body, bool Internal);

/// <param name="AssignedTo">An admin's account id, an empty string for nobody, or null to leave it.</param>
internal record AdminSuggestionUpdate(string? Status, string? AssignedTo);

/// <param name="Configured">An admin because the site's configuration names their address, which the admin area cannot change.</param>
internal record AdminUserResponse(
    Guid Id,
    string Name,
    string? Photo,
    string? Email,
    DateTimeOffset CreatedAt,
    bool Admin,
    bool Configured,
    int Suggestions);

internal record AdminUsersResponse(IReadOnlyList<AdminUserResponse> Items, int Total);

internal record AdminGrant(bool Admin);

internal record AdminNameResponse(Guid Id, string Name);

internal record AdminsResponse(IReadOnlyList<AdminNameResponse> Items);

internal record AdminActionResponse(
    long Id,
    DateTimeOffset At,
    string ActorName,
    string Action,
    Guid? SuggestionId,
    Guid? AccountId,
    string? AccountName,
    string? Before,
    string? After);

internal record AdminActionsResponse(IReadOnlyList<AdminActionResponse> Items, int Total);
