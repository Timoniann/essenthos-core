using System.Text.Json;
using Essenthos.Core.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Each of a reader's devices, with its own settings and its own reading, and the one thing that
/// crosses between them: "you were reading John 3 on your phone — continue here?".
///
///     POST   /v1/me/device                     this session's device: the one whose id the browser kept, or new
///     PUT    /v1/me/device/settings            its settings, if newer than the server's copy
///     POST   /v1/me/device/reading             the chapter it is showing
///     GET    /v1/me/devices                    every device, with what it last read
///     GET    /v1/me/devices/{id}/readings      one device's history
///     DELETE /v1/me/devices/{id}/readings      forget it
///     DELETE /v1/me/devices/{id}               forget the device, and sign it out
///     POST   /v1/me/devices/{id}/sign-out      sign the device out, keeping its history
///     GET    /v1/me/continue                   what another device read more recently than this one
/// </summary>
internal static class DeviceEndpoints
{
    /// <summary>How long another device's reading is worth offering to continue.</summary>
    private static readonly TimeSpan ContinueWithin = TimeSpan.FromDays(14);

    /// <summary>
    /// Moving between chapters in one sitting is one entry that moves, not one per chapter — a history
    /// of every page turned is neither useful to read nor worth holding.
    /// </summary>
    private static readonly TimeSpan SameSitting = TimeSpan.FromMinutes(30);

    /// <summary>The longest history a device keeps; older entries are dropped as new ones arrive.</summary>
    private const int HistoryLimit = 200;

    /// <summary>A settings object larger than this is not a set of preferences.</summary>
    private const int SettingsLimit = 16 * 1024;

    public static void MapDevices(this IEndpointRouteBuilder routes)
    {
        var me = routes.MapGroup("/me").RequireAuthorization();

        me.MapPost("/device", async (HttpContext context, AccountsDbContext db, DeviceProfile profile) =>
        {
            if (Clean(profile) is not { } described)
            {
                return Results.BadRequest(new ProblemResponse("A device is a kind, a system and a browser."));
            }

            var account = context.User.AccountId();
            var sessionId = context.User.SessionId();
            var session = await db.Sessions.FirstAsync(s => s.Id == sessionId, context.RequestAborted);
            var now = DateTimeOffset.UtcNow;

            var device = session.DeviceId is { } bound
                ? await db.Devices.FirstOrDefaultAsync(d => d.Id == bound && d.AccountId == account, context.RequestAborted)
                : null;

            // A session that has no device yet is the device whose id this browser kept, if it kept one
            // and it is this account's. Otherwise it is a new device: nothing is matched by what a
            // device looks like, because two laptops in one browser look the same.
            if (device is null && profile.Id is { } kept)
            {
                device = await db.Devices.FirstOrDefaultAsync(d => d.Id == kept && d.AccountId == account, context.RequestAborted);
            }

            if (device is null)
            {
                device = new Device
                {
                    Id = Guid.CreateVersion7(),
                    AccountId = account,
                    Kind = described.Kind,
                    Os = described.Os,
                    Browser = described.Browser,
                    Model = described.Model,
                    CreatedAt = now,
                };
                db.Devices.Add(device);
            }

            device.LastSeenAt = now;
            device.SignedOutAt = null;
            session.DeviceId = device.Id;
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(Describe(device, current: true));
        });

        me.MapPut("/device/settings", async (HttpContext context, AccountsDbContext db, DeviceSettingsUpdate update) =>
        {
            if (update.Settings.ValueKind != JsonValueKind.Object || update.Settings.GetRawText().Length > SettingsLimit)
            {
                return Results.BadRequest(new ProblemResponse("Settings are one JSON object, at most 16 KB."));
            }

            if (await Current(context, db) is not { } device)
            {
                return NoDevice();
            }

            // The newer copy wins, so a device that was offline and changed nothing cannot overwrite what
            // it changed later elsewhere in the same browser.
            if (device.SettingsChangedAt is null || update.ChangedAt > device.SettingsChangedAt)
            {
                device.Settings = update.Settings.GetRawText();
                device.SettingsChangedAt = update.ChangedAt > DateTimeOffset.UtcNow ? DateTimeOffset.UtcNow : update.ChangedAt;
                await db.SaveChangesAsync(context.RequestAborted);
            }

            return Results.Ok(Describe(device, current: true));
        });

        me.MapPost("/device/reading", async (HttpContext context, AccountsDbContext db, ReadingUpdate update) =>
        {
            var corpora = string.Join(',', update.Corpora ?? []);
            if (update.Book is not { Length: > 0 and <= 32 } book || update.Chapter is < 1 or > 200 || corpora.Length > 200)
            {
                return Results.BadRequest(new ProblemResponse("A reading is a book, a chapter and the texts open."));
            }

            if (await Current(context, db) is not { } device)
            {
                return NoDevice();
            }

            var now = DateTimeOffset.UtcNow;
            var last = await db.Readings
                .Where(r => r.DeviceId == device.Id)
                .OrderByDescending(r => r.At)
                .FirstOrDefaultAsync(context.RequestAborted);

            if (last is not null && now - last.At < SameSitting && last.Book == book)
            {
                last.Chapter = update.Chapter;
                last.Corpora = corpora;
                last.At = now;
            }
            else
            {
                db.Readings.Add(new Reading
                {
                    AccountId = device.AccountId, DeviceId = device.Id, Book = book, Chapter = update.Chapter,
                    Corpora = corpora, At = now,
                });
            }

            device.LastSeenAt = now;
            await db.SaveChangesAsync(context.RequestAborted);

            var stale = await db.Readings.Where(r => r.DeviceId == device.Id)
                .OrderByDescending(r => r.At).Skip(HistoryLimit).Select(r => r.Id)
                .ToListAsync(context.RequestAborted);
            if (stale.Count > 0)
            {
                await db.Readings.Where(r => stale.Contains(r.Id)).ExecuteDeleteAsync(context.RequestAborted);
            }

            return Results.NoContent();
        });

        me.MapGet("/devices", async (HttpContext context, AccountsDbContext db) =>
        {
            var account = context.User.AccountId();
            var current = await CurrentId(context, db);
            var devices = await db.Devices.AsNoTracking()
                .Where(d => d.AccountId == account)
                .OrderByDescending(d => d.LastSeenAt)
                .ToListAsync(context.RequestAborted);
            var ids = devices.Select(d => d.Id).ToList();

            // The latest reading of each device, in one query rather than one per device.
            var latest = await db.Readings.AsNoTracking()
                .Where(r => ids.Contains(r.DeviceId))
                .GroupBy(r => r.DeviceId)
                .Select(g => g.OrderByDescending(r => r.At).First())
                .ToListAsync(context.RequestAborted);
            var byDevice = latest.ToDictionary(r => r.DeviceId);

            return Results.Ok(new DevicesResponse(devices
                .Select(d => new DeviceSummaryResponse(
                    d.Id, Label(d), d.Kind, d.Id == current, d.SignedOutAt is null, d.LastSeenAt,
                    byDevice.TryGetValue(d.Id, out var reading) ? Describe(reading) : null))
                .ToList()));
        });

        me.MapGet("/devices/{id:guid}/readings", async (HttpContext context, AccountsDbContext db, Guid id, int? take) =>
        {
            var account = context.User.AccountId();
            var readings = await db.Readings.AsNoTracking()
                .Where(r => r.DeviceId == id && r.AccountId == account)
                .OrderByDescending(r => r.At)
                .Take(Math.Clamp(take ?? 50, 1, HistoryLimit))
                .ToListAsync(context.RequestAborted);
            return Results.Ok(new ReadingsResponse(readings.Select(Describe).ToList()));
        });

        me.MapDelete("/devices/{id:guid}/readings", async (HttpContext context, AccountsDbContext db, Guid id) =>
        {
            var account = context.User.AccountId();
            await db.Readings.Where(r => r.DeviceId == id && r.AccountId == account).ExecuteDeleteAsync(context.RequestAborted);
            return Results.NoContent();
        });

        // Forgetting a device signs it out as well: a device the reader has disowned should not stay
        // signed in to their account.
        me.MapDelete("/devices/{id:guid}", async (HttpContext context, AccountsDbContext db, Guid id) =>
        {
            var account = context.User.AccountId();
            await db.Sessions.Where(s => s.DeviceId == id && s.AccountId == account).ExecuteDeleteAsync(context.RequestAborted);
            var removed = await db.Devices.Where(d => d.Id == id && d.AccountId == account).ExecuteDeleteAsync(context.RequestAborted);
            return removed == 0 ? Results.NotFound(new ProblemResponse("No such device.")) : Results.NoContent();
        });

        me.MapPost("/devices/{id:guid}/sign-out", async (HttpContext context, AccountsDbContext db, Guid id) =>
        {
            var account = context.User.AccountId();
            if (!await db.Devices.AnyAsync(d => d.Id == id && d.AccountId == account, context.RequestAborted))
            {
                return Results.NotFound(new ProblemResponse("No such device."));
            }

            await db.Sessions.Where(s => s.DeviceId == id && s.AccountId == account).ExecuteDeleteAsync(context.RequestAborted);
            await SignedOut(db, id, context.RequestAborted);
            return Results.NoContent();
        });

        me.MapGet("/continue", async (HttpContext context, AccountsDbContext db) =>
        {
            var account = context.User.AccountId();
            var current = await CurrentId(context, db);
            var since = DateTimeOffset.UtcNow - ContinueWithin;

            var here = current is null
                ? null
                : await db.Readings.AsNoTracking().Where(r => r.DeviceId == current)
                    .OrderByDescending(r => r.At).Select(r => (DateTimeOffset?)r.At).FirstOrDefaultAsync(context.RequestAborted);

            var elsewhere = await db.Readings.AsNoTracking()
                .Where(r => r.AccountId == account && r.DeviceId != current && r.At > since)
                .OrderByDescending(r => r.At)
                .FirstOrDefaultAsync(context.RequestAborted);

            // Only what is newer than this device's own reading: having read John 3 on the phone
            // yesterday and Romans 8 here this morning, there is nothing to continue.
            if (elsewhere is null || (here is { } mine && mine >= elsewhere.At))
            {
                return Results.NoContent();
            }

            var device = await db.Devices.AsNoTracking().FirstAsync(d => d.Id == elsewhere.DeviceId, context.RequestAborted);
            return Results.Ok(new ContinueResponse(device.Id, Label(device), device.Kind, Describe(elsewhere)));
        });
    }

    /// <summary>
    /// Marks a device signed out when no session is left on it. Called wherever a session ends — signing
    /// out on the device, ending its session from another, signing the device out — so the list of
    /// devices says truthfully which are still signed in.
    /// </summary>
    public static async Task SignedOut(AccountsDbContext db, Guid? device, CancellationToken cancellationToken)
    {
        if (device is not { } id || await db.Sessions.AnyAsync(s => s.DeviceId == id, cancellationToken))
        {
            return;
        }

        var record = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (record is not null && record.SignedOutAt is null)
        {
            record.SignedOutAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<Device?> Current(HttpContext context, AccountsDbContext db) =>
        await CurrentId(context, db) is { } id
            ? await db.Devices.FirstOrDefaultAsync(d => d.Id == id, context.RequestAborted)
            : null;

    private static async Task<Guid?> CurrentId(HttpContext context, AccountsDbContext db)
    {
        var session = context.User.SessionId();
        return await db.Sessions.Where(s => s.Id == session).Select(s => s.DeviceId).FirstOrDefaultAsync(context.RequestAborted);
    }

    private static IResult NoDevice() =>
        Results.Json(new ProblemResponse("This session has not described its device yet: POST /v1/me/device first."),
            statusCode: StatusCodes.Status409Conflict);

    private static readonly string[] Kinds = ["mobile", "tablet", "desktop"];

    /// <summary>The profile, trimmed to what the columns hold, or null if it names no device.</summary>
    internal static DescribedDevice? Clean(DeviceProfile profile)
    {
        static string? Part(string? value, int limit) =>
            value?.Trim() is { Length: > 0 } text ? text.Length > limit ? text[..limit] : text : null;

        var kind = profile.Kind?.Trim().ToLowerInvariant();
        return kind is not null && Kinds.Contains(kind) && Part(profile.Os, 32) is { } os && Part(profile.Browser, 32) is { } browser
            ? new DescribedDevice(kind, os, browser, Part(profile.Model, 64))
            : null;
    }

    /// <summary>"Pixel 8 · Chrome", "Chrome on Windows" — what a reader would call it.</summary>
    internal static string Label(Device device) =>
        device.Model is { } model ? $"{model} · {device.Browser}" : $"{device.Browser} on {device.Os}";

    private static DeviceResponse Describe(Device device, bool current) => new(
        device.Id,
        Label(device),
        device.Kind,
        current,
        device.Settings is { } settings ? JsonDocument.Parse(settings).RootElement.Clone() : null,
        device.SettingsChangedAt);

    private static ReadingResponse Describe(Reading reading) =>
        new(reading.Book, reading.Chapter, reading.Corpora.Length == 0 ? [] : reading.Corpora.Split(','), reading.At);
}

/// <param name="Kind"><c>mobile</c>, <c>tablet</c> or <c>desktop</c> — to name the device, not to find it.</param>
/// <param name="Model">Only where the browser reports it — Android, through client hints.</param>
/// <param name="Id">The id this browser was given the last time it was signed in here, if it kept it.</param>
internal record DeviceProfile(string? Kind, string? Os, string? Browser, string? Model, Guid? Id = null);

/// <summary>A <see cref="DeviceProfile"/> that has been checked and trimmed.</summary>
internal sealed record DescribedDevice(string Kind, string Os, string Browser, string? Model);

/// <param name="Settings">The device's settings as the client keeps them, or null if it has sent none.</param>
internal record DeviceResponse(
    Guid Id, string Label, string Kind, bool Current, JsonElement? Settings, DateTimeOffset? SettingsChangedAt);

internal record DeviceSettingsUpdate(JsonElement Settings, DateTimeOffset ChangedAt);

internal record ReadingUpdate(string? Book, int Chapter, IReadOnlyList<string>? Corpora);

internal record ReadingResponse(string Book, int Chapter, IReadOnlyList<string> Corpora, DateTimeOffset At);

internal record ReadingsResponse(IReadOnlyList<ReadingResponse> Items);

/// <param name="SignedIn">False once the device was signed out; its history is kept until it is forgotten.</param>
internal record DeviceSummaryResponse(
    Guid Id, string Label, string Kind, bool Current, bool SignedIn, DateTimeOffset LastSeenAt, ReadingResponse? LastReading);

internal record DevicesResponse(IReadOnlyList<DeviceSummaryResponse> Items);

internal record ContinueResponse(Guid DeviceId, string Label, string Kind, ReadingResponse Reading);
