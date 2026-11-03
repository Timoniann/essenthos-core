using Essenthos.Core.Configuration;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Conversion;

/// <summary>
/// Writes a text's corrections file once from the repairs the load still computes in code: bible4u's King
/// James against the 1769 text, the Synodal's list, Ohienko's title, the psalm openings and the verse
/// endings. After this the file is what the converter reads, and the code it was written from goes with
/// the old load. A repair that only changes a word's letters becomes a <c>letters</c> row on that word;
/// one that changes how a verse divides into words stays a <c>verse</c> row on the source's verse.
/// </summary>
internal static class CorrectionSeeds
{
    private const string KingJamesWhy = "bible4u's file against eBible's eng-kjv2006, the standardised 1769 text";
    private const string KingJamesCapitalsWhy = "the 1769 text (eng-kjv2006) prints it in capitals";
    private const string SubscriptionWhy = "the epistle's subscription, which bible4u's file does not print, from eng-kjv2006";
    private const string SuperscriptionWhy = "the psalm's superscription, which bible4u's file does not print, from eng-kjv2006";
    private const string OhienkoTitleWhy = "the title as the 1988 printing spells it (Ukrainian Wikisource), without stress marks";
    private const string OhienkoLineWhy = "the line of the psalm the file lost, from the 1988 printing (Ukrainian Wikisource), without stress marks";
    private const string OhienkoEndingWhy = "the end of the verse the file cut short, from the 1988 printing (Ukrainian Wikisource), without stress marks, quotation marks and dashes";

    public static string Write(ProductStore store, string slug)
    {
        var resources = store.ResourcesPath;
        var identifier = Bible4uTextSource.Definitions.FirstOrDefault(d => d.Value.Slug == slug).Key
                         ?? throw new ArgumentException($"{slug} has no repairs computed in code; only bible4u's KJV, RUSV and UBIO do.");
        var path = ResourcePaths.File(resources, "bible4u", $"{identifier}.xml");
        var bible = new XmlBibleParser().Parse(File.ReadAllText(path));
        var repairs = Bible4uRepairs.For(identifier, bible, resources);

        var synodal = slug == Bible4uTextSource.Synodal
            ? SynodalCorrections.All.ToLookup(c => (c.Book, c.Chapter, c.Verse), c => c.Kind)
            : null;
        string Why(VerseRepair repair) => slug switch
        {
            Bible4uTextSource.KingJames => KingJamesWhy,
            Bible4uTextSource.Ohienko => OhienkoTitleWhy,
            _ => $"bible4u's file against the Strong-tagged digitisation swmail/RST: "
                 + string.Join(", ", synodal![(repair.Book, repair.Chapter, repair.Verse)].Distinct()),
        };

        // An epistle's subscription, which the King James's repair puts after the verse, is a row of its own, so the
        // words the source prints and the words added to them stay apart.
        var subscriptions = slug == Bible4uTextSource.KingJames
            ? KingJamesRepairs.Edition(Path.Combine(resources, LostPsalmOpenings.KingJamesFolder)).Subscriptions
            : [];
        var rows = new List<TextCorrection>();
        foreach (var repair in repairs.Verses.OrderBy(r => (r.Book, r.Chapter, r.Verse)))
        {
            var verse = repair;
            TextCorrection? subscribed = null;
            if (subscriptions.TryGetValue((repair.Book, repair.Chapter, repair.Verse), out var subscription)
                && repair.Printed.EndsWith($" {subscription}", StringComparison.Ordinal))
            {
                verse = repair with { Printed = repair.Printed[..^(subscription.Length + 1)] };
                subscribed = new TextCorrection(new VerseKey(repair.Book, repair.Chapter, repair.Verse).ToString(), TextCorrection.Append, null,
                    subscription, SubscriptionWhy);
            }

            if (verse.Printed != verse.Digitised)
            {
                rows.AddRange(Rows(slug, verse, Why(verse)));
            }

            if (subscribed is not null)
            {
                rows.Add(subscribed);
            }
        }

        if (slug == Bible4uTextSource.KingJames)
        {
            foreach (var opening in LostPsalmOpenings.KingJamesSuperscriptions(Path.Combine(resources, LostPsalmOpenings.KingJamesFolder)))
            {
                rows.Add(Added(new VerseKey(19, opening.Psalm, 1), TextCorrection.Prepend, opening.Words, SuperscriptionWhy));
            }
        }

        if (slug == Bible4uTextSource.Ohienko)
        {
            foreach (var opening in LostPsalmOpenings.OhienkoLostLine(Path.Combine(resources, LostPsalmOpenings.OhienkoFolder)))
            {
                rows.Add(Added(new VerseKey(19, opening.Psalm, 1),
                    opening.Place == PsalmOpeningPlace.BeforeTheVerse ? TextCorrection.Prepend : TextCorrection.Append, opening.Words, OhienkoLineWhy));
            }

            var read = Bible4uTextSource.Build(bible, Bible4uTextSource.Definitions[identifier], repairs);
            foreach (var ending in LostVerseEndings.OhienkoVerses(Path.Combine(resources, LostPsalmOpenings.OhienkoFolder)))
            {
                var verse = read.Books.Single(b => b.CanonicalOrdinal == ending.Book).Chapters.Single(c => c.Number == ending.Chapter)
                    .Verses.Single(v => v.Number == ending.Verse && v.Label.Length == 0);
                var stored = string.Concat(verse.Words.Select(w => w.Surface + w.Trailer)).TrimEnd();
                if (!ending.Complete.StartsWith(stored, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{slug} {ending.Book}.{ending.Chapter}:{ending.Verse} reads \"{stored}\", not the head of \"{ending.Complete}\".");
                }

                var tail = ending.Complete[stored.Length..].Trim();
                rows.Add(new TextCorrection(new VerseKey(ending.Book, ending.Chapter, ending.Verse).ToString(), TextCorrection.Append, null, tail, OhienkoEndingWhy));
            }
        }

        using var writer = store.Create(TextCorrections.ProductOf(slug), ProductKind.Hand, TextCorrection.Schema,
            ProductHeader.DescribeProducer($"forge texts corrections {slug}"), store.Inputs($"bible4u/{identifier}.xml"),
            header =>
            {
                header.Notes.Add(new("kinds", "verse: the source's verse (markup and all) is read as after; letters: the word's letters before become after; "
                                              + "prepend/append: the words of after open/close the verse; drop: the word reading before goes"));
                header.Notes.Add(new("note", "written once from the repairs the load computed in code; edit this file, not the code"));
            });
        writer.WriteAll(rows);
        return $"{slug}: {rows.Count} corrections ({string.Join(", ", rows.GroupBy(r => r.Kind).Select(g => $"{g.Count()} {g.Key}"))})";
    }

    /// <summary>
    /// The rows one verse repair becomes: a <c>letters</c> row per word whose letters alone change, when the
    /// repaired verse divides into the same words with the same trailers, spans and marks; a <c>verse</c> row otherwise.
    /// </summary>
    private static IEnumerable<TextCorrection> Rows(string slug, VerseRepair repair, string why)
    {
        var before = repair.Before;
        var after = repair.After;
        var key = new VerseKey(repair.Book, repair.Chapter, repair.Verse);
        var sameShape = before.Count == after.Count
                        && before.Zip(after).All(pair => pair.First.Trailer == pair.Second.Trailer && pair.First.SuppliedSpan == pair.Second.SuppliedSpan)
                        && VerseWords.StatedAddresses(repair.Digitised).SequenceEqual(VerseWords.StatedAddresses(repair.Printed))
                        && VerseWords.MarksASuperscription(repair.Digitised) == VerseWords.MarksASuperscription(repair.Printed)
                        && VerseWords.OpensBeforeItsStatedAddress(repair.Digitised) == VerseWords.OpensBeforeItsStatedAddress(repair.Printed);
        if (!sameShape)
        {
            yield return new TextCorrection(key.ToString(), TextCorrection.Verse, repair.Digitised, repair.Printed, why);
            yield break;
        }

        for (var at = 0; at < before.Count; at++)
        {
            if (before[at].Word != after[at].Word)
            {
                var capitals = slug == Bible4uTextSource.KingJames && string.Equals(before[at].Word, after[at].Word, StringComparison.OrdinalIgnoreCase);
                yield return new TextCorrection(new WordKey(slug, key, at + 1).Own, TextCorrection.Letters, before[at].Word, after[at].Word,
                    capitals ? KingJamesCapitalsWhy : why);
            }
        }
    }

    /// <summary>A prepend or append row, refused unless the converter divides it back into exactly these words.</summary>
    private static TextCorrection Added(VerseKey key, string kind, IReadOnlyList<WordDraft> words, string why)
    {
        var row = new TextCorrection(key.ToString(), kind, null, string.Concat(words.Select(w => w.Surface + w.Trailer)), why);
        var divided = TextCorrections.Words(row);
        if (!divided.Select(w => (w.Surface, w.Trailer)).SequenceEqual(words.Select(w => (w.Surface, w.Trailer))))
        {
            throw new InvalidOperationException(
                $"The words added at {key} do not divide back as written: [{string.Join("|", words.Select(w => w.Surface + "/" + w.Trailer))}] "
                + $"read [{string.Join("|", divided.Select(w => w.Surface + "/" + w.Trailer))}].");
        }

        return row;
    }
}
