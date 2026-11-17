using System.Globalization;
using Essenthos.Core.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <param name="Sense">Dillmann's number for the sense, <c>2a</c>; <c>main</c> for the sense his entry opens with.</param>
/// <param name="Text">The machine rendering of his Latin for that sense.</param>
/// <param name="Language">The language <paramref name="Text"/> is in: the reader's, or English where none was made in it.</param>
internal record GeezRenderedSenseResponse(string Sense, string Text, string Language);

/// <param name="Entry">The digital edition's identifier of the entry, as a Ge'ez word's <c>geezEntry.entry</c> names it.</param>
/// <param name="Model">The model that rendered it, as its maker names it: <c>claude-sonnet-5-5</c>.</param>
/// <param name="Made">The day it was rendered, <c>2026-11-16</c>.</param>
internal record GeezRenderingResponse(string Entry, string Headword, IList<GeezRenderedSenseResponse> Senses, string Model, string Made);

/// <summary>
/// Dillmann's entry rendered from his Latin into the reader's language by a language model, sense by sense, for the word
/// card to show under his Latin as what it is: a machine rendering. Asked for only when a card with his entry opens.
/// </summary>
internal static class GeezEndpoints
{
    public static void MapGeez(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/geez/dillmann/{entry}", async (
            string entry,
            [FromQuery] string? language,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var senses = await db.GeezLexiconRenderings
                .Where(r => r.Entry == entry)
                .OrderBy(r => r.Ordinal)
                .ToListAsync(cancellationToken);
            if (senses.Count == 0)
            {
                return Results.NotFound(new ProblemResponse($"Dillmann's entry \"{entry}\" has no rendering."));
            }

            var headword = await db.GeezLexiconEntries.Where(e => e.Entry == entry).Select(e => e.Headword).FirstOrDefaultAsync(cancellationToken)
                           ?? string.Empty;
            return Results.Ok(new GeezRenderingResponse(
                entry,
                headword,
                [
                    .. senses.Select(sense => (Sense: sense, Text: In(sense, language)))
                        .Where(rendered => rendered.Text is not null)
                        .Select(rendered => new GeezRenderedSenseResponse(rendered.Sense.Sense, rendered.Text!.Value.Text, rendered.Text.Value.Language)),
                ],
                senses[0].Model,
                senses[0].Made.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }).KeptUntilRelease();
    }

    /// <summary>The sense in the reader's language, else in English, else in whichever language it was rendered; null for none.</summary>
    private static (string Text, string Language)? In(Database.Entities.GeezLexiconRendering sense, string? language)
    {
        var asked = language switch
        {
            "ukr" => (sense.Uk, "ukr"),
            "deu" => (sense.De, "deu"),
            "spa" => (sense.Es, "spa"),
            _ => (sense.En, "eng"),
        };
        return asked.Item1 is { } text ? (text, asked.Item2)
            : sense.En is { } english ? (english, "eng")
            : sense.Uk is { } ukrainian ? (ukrainian, "ukr")
            : sense.De is { } german ? (german, "deu")
            : sense.Es is { } spanish ? (spanish, "spa")
            : null;
    }
}
