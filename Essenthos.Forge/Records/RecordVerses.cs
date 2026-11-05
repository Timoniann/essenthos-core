using System.Collections.Concurrent;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Names;
using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

/// <summary>
/// The verses our words and relationships put a record in, <c>records/derived/verses</c>, from the names the projection
/// puts on the words of every held text (<c>names/projected</c>), where each word's verse stands (<c>texts/*/frame</c>) and
/// the relationships (<c>records/facts/relationships</c>). It is what the old load's passes left in <c>entity_verse</c> under
/// their two sources, made again from files on every change of a name:
/// <list type="bullet">
/// <item>every record a word names, as the reader is told it (<see cref="Annotations.Settle"/>), at the primary canonical
/// address of the word's verse, once however many words and texts name it there: a person, a place, a title, an object or an
/// observance under <see cref="OwnReferences"/> with no label, a people under <see cref="PeopleReferences"/> with its name as
/// the label (<see cref="OwnReferenceLoader"/>, <see cref="PeopleLoader"/>);</item>
/// <item>withheld where every word naming a man in the verse is the name after <em>the sons of</em> that the owner's ruling
/// reads as the people beside a word naming the people in the same verse of the same text (<see cref="Annotations.SonsOf"/>);</item>
/// <item>for a record no other verse lists, the verses its relationships were read from (<see cref="RelationshipVerseLoader"/>),
/// naming it where a word of the verse carries it in any text;</item>
/// <item>every verse the gazetteer lists for the record its row is filed under (<c>external/openbible/references</c>), under
/// its own credit as <see cref="OpenBiblePlaceLoader"/> stored it, naming the record where a word of the verse carries it in
/// any text. BibleData's verses stay its references only.</item>
/// </list>
/// A term such a row lists is named where a word of the verse, in any text, carries one of its Strong numbers, as
/// <see cref="VerseNaming.Sql"/> reads it: no word is annotated to a term.
/// A base reading beside a differs is the base text's name, not the word's, and puts no verse on a page.
/// </summary>
internal static class RecordVerses
{
    public const string Product = "records/derived/verses";

    public const string Producer = "forge records-verses";

    public const string Version = "2";

    public const string VersionNote = "verses-version";

    private const string OwnReferences = "Essenthos, from the words this corpus annotates to the person or the place they name";

    private const string PeopleReferences = PeopleLoader.FromOurOwnWords;

    private const string RelationshipReferences = RelationshipVerseLoader.Source;

    private const string GazetteerReferences = OpenBiblePlaceLoader.Source;

    private const string Gazetteer = "external/openbible/references";

    /// <summary>The kinds whose verses are read off the words; the words for God are read off their Strong numbers elsewhere.</summary>
    private static readonly IReadOnlySet<EntityKind> Named =
        new HashSet<EntityKind> { EntityKind.Person, EntityKind.Place, EntityKind.Title, EntityKind.Object, EntityKind.Observance };

    /// <summary>The files besides the names and the frames that the verses are read from.</summary>
    private static readonly string[] Facts =
    [
        "records/records", "records/names", "records/facts/titles", "records/facts/relationships", "records/facts/verses", Gazetteer,
    ];

    public sealed record Outcome(bool Written, long Rows, long Records, long Names, long Withheld, long FromRelationships, long FromGazetteer);

    /// <summary>A name on a word, as the projection or a database's export gives it.</summary>
    /// <param name="Role">Null where the database's row predates roles.</param>
    /// <param name="Carried">Carried along a link from another text's word.</param>
    internal sealed record WordName(WordKey Own, string Record, string? Role, bool Carried, string Method, double? Confidence, string Source);

    /// <summary>Writes <c>records/derived/verses</c> when an input moved or the rule's version did, or when forced.</summary>
    public static Outcome Write(ProductStore store, bool force)
    {
        var texts = Texts(store, "projected");
        string[] read =
        [
            .. texts.Select(NameProducts.Projected),
            .. texts.Select(FrameFile.Product),
            .. texts.Select(TextWord.ProductOf),
            .. Facts,
        ];
        var inputs = store.Inputs(read.Select(store.Find).OfType<string>());
        if (!force && !NeedsBuild(store, inputs))
        {
            return new Outcome(false, 0, 0, 0, 0, 0, 0);
        }

        var (rows, outcome) = Derive(store, texts, text =>
        {
            using var reader = store.Open(NameProducts.Projected(text), ProjectedName.Schema(text));
            return [.. reader.Rows().Where(n => NameOrigins.IsOwn(n.Origin))
                .Select(n => new WordName(n.Own, n.Record, n.Role, n.Through is not null, n.Method, n.Confidence, n.Source))];
        });
        var file = RecordFiles.All.Single(f => f.Product == Product);
        using var writer = store.Create(Product, file.Kind, file.Schema(), ProductHeader.DescribeProducer(Producer), inputs,
            header =>
            {
                header.Notes.Add(new(VersionNote, Version));
                header.Comments.Add(file.About!);
            });
        writer.WriteAll(rows);
        writer.Complete();
        return outcome with { Written = true };
    }

    /// <summary>
    /// The rows over a database's own names as <c>names/live</c> holds them (no roles), written as TSV to
    /// <paramref name="path"/> outside the store: what the rule makes of the names a load left, to hold against that
    /// database's export.
    /// </summary>
    public static Outcome WriteOverLive(ProductStore store, string path)
    {
        var texts = Texts(store, "live");
        var (rows, outcome) = Derive(store, texts, text =>
        {
            using var reader = store.Open(NameProducts.Live(text), LiveName.Schema(text));
            return [.. reader.Rows().Select(n => new WordName(n.Own, n.Record, null, n.Carried, n.Method, n.Confidence, n.Source))];
        });
        var file = RecordFiles.All.Single(f => f.Product == Product);
        using var output = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
        output.WriteLine(string.Join('\t', file.Parsed.Select(c => c.Name)));
        foreach (var row in rows)
        {
            output.WriteLine(string.Join('\t', row.Select(value => value ?? "\\N")));
        }

        return outcome with { Written = true };
    }

    private static (List<string?[]> Rows, Outcome Outcome) Derive(ProductStore store, List<string> texts, Func<string, List<WordName>> names)
    {
        var records = Records(store);
        var ids = records.Select((r, i) => (r.Slug, i)).ToDictionary(p => p.Slug, p => p.i, StringComparer.Ordinal);
        var bearers = Read(store, "records/facts/titles")
            .Where(r => ids.ContainsKey(r[0]!) && ids.ContainsKey(r[1]!))
            .Select(r => (ids[r[0]!], ids[r[1]!]))
            .ToHashSet();

        var gazetteerRows = store.Exists(Gazetteer) ? Read(store, Gazetteer) : [];
        var relationshipRows = Read(store, "records/facts/relationships")
            .Where(r => r[3] is not null && !r[8]!.StartsWith(ShownVerses.Witness, StringComparison.Ordinal))
            .ToList();
        var terms = Terms(store, records, ids,
            gazetteerRows.Select(r => r[1]!).Concat(relationshipRows.SelectMany(r => new[] { r[0]!, r[2]! })));

        // (record, canonical verse) => whether every word naming the record there stands apart for its people.
        var named = new ConcurrentDictionary<(int Record, VerseKey Verse), bool>();
        var anyName = new ConcurrentDictionary<(int Record, VerseKey Verse), byte>();
        var apart = new ConcurrentDictionary<int, byte>();
        long read = 0;
        Parallel.ForEach(texts, new ParallelOptions { MaxDegreeOfParallelism = 8 }, text =>
        {
            var frame = Frame(store, text);
            VerseKey Placed(VerseKey own) => frame.TryGetValue(own, out var address) ? address : FrameFile.AtOwn(own);

            var rows = names(text).Where(n => ids.ContainsKey(n.Record)).ToList();
            Interlocked.Add(ref read, rows.Count);
            var words = new Dictionary<WordKey, long>();
            var keys = new List<WordKey>();
            var claimed = new List<Annotations.Claimed>(rows.Count);
            var sonsOf = new HashSet<(long Word, int Record)>();
            foreach (var row in rows)
            {
                if (!words.TryGetValue(row.Own, out var word))
                {
                    words[row.Own] = word = keys.Count;
                    keys.Add(row.Own);
                }

                var id = ids[row.Record];
                var record = records[id];
                claimed.Add(new Annotations.Claimed(
                    word, EnumSpelling.ToLinkMethod(row.Method), row.Confidence, row.Source, row.Carried ? "through another word" : null,
                    record.Kind, record.Slug, record.Slug)
                {
                    EntityId = id,
                    Role = row.Role,
                });
                anyName.TryAdd((id, Placed(row.Own.Verse)), 0);
                if (row.Source == Annotations.SonsOf)
                {
                    sonsOf.Add((word, id));
                }
            }

            if (terms.Count > 0 && store.Exists(TextWord.ProductOf(text)))
            {
                using var numbered = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
                foreach (var numberedWord in numbered.Rows())
                {
                    if (numberedWord.Strong is { } strong && terms.TryGetValue(strong, out var bearing))
                    {
                        foreach (var term in bearing)
                        {
                            anyName.TryAdd((term, Placed(numberedWord.Key.Verse)), 0);
                        }
                    }
                }
            }

            var settled = Annotations.Settle(claimed, bearers);

            // A man named by the ruling on 'the sons of' in a verse of this text where the ruling names a people too.
            var ruled = settled.SelectMany(w => w.Value.Where(c => sonsOf.Contains((w.Key, c.EntityId))).Select(c => (Word: w.Key, c.EntityId, c.Kind)))
                .ToList();
            var peopleVerses = ruled.Where(r => r.Kind == EntityKind.People).Select(r => keys[(int)r.Word].Verse).ToHashSet();
            var standsApart = ruled.Where(r => r.Kind == EntityKind.Person && peopleVerses.Contains(keys[(int)r.Word].Verse))
                .Select(r => (r.Word, r.EntityId)).ToHashSet();
            foreach (var (_, record) in standsApart)
            {
                apart.TryAdd(record, 0);
            }

            foreach (var (word, answers) in settled)
            {
                var verse = Placed(keys[(int)word].Verse);
                foreach (var answer in answers)
                {
                    var isApart = standsApart.Contains((word, answer.EntityId));
                    named.AddOrUpdate((answer.EntityId, verse), isApart, (_, all) => all && isApart);
                }
            }
        });

        var rows = new List<string?[]>();
        long withheld = 0;
        foreach (var ((id, verse), allApart) in named)
        {
            var record = records[id];
            if (record.Kind == EntityKind.People)
            {
                rows.Add([record.Slug, verse.ToString(), record.Name, "t", "f", "f", PeopleReferences]);
            }
            else if (Named.Contains(record.Kind))
            {
                var held = allApart && apart.ContainsKey(id);
                withheld += held ? 1 : 0;
                rows.Add([record.Slug, verse.ToString(), null, "t", "f", held ? "t" : "f", OwnReferences]);
            }
        }

        string? Names(string slug, string verse) => anyName.ContainsKey((ids[slug], VerseKey.Parse(verse))) ? "t" : "f";

        // The gazetteer's rows: external_id record ref label names disputed withheld.
        var gazetteer = gazetteerRows
            .Where(r => ids.ContainsKey(r[1]!))
            .Select(r => new string?[] { r[1], r[2], r[3], Names(r[1]!, r[2]!), r[5], r[6], GazetteerReferences })
            .ToList();

        // A record is listed where any verse a reader is shown lists it: ours read off the words, a reading of ours by hand,
        // or the gazetteer's.
        var listed = rows.Select(r => r[0]!)
            .Concat(Read(store, "records/facts/verses").Select(r => r[0]!))
            .Concat(gazetteer.Select(r => r[0]!))
            .ToHashSet(StringComparer.Ordinal);
        var fromRelationships = relationshipRows
            .SelectMany(r => new[] { (Slug: r[0]!, Ref: r[3]!), (Slug: r[2]!, Ref: r[3]!) })
            .Where(r => ids.ContainsKey(r.Slug) && !listed.Contains(r.Slug))
            .Distinct()
            .Select(r => new string?[]
            {
                r.Slug, r.Ref, null, Names(r.Slug, r.Ref), "f", "f", RelationshipReferences,
            })
            .ToList();
        rows.AddRange(fromRelationships);
        rows.AddRange(gazetteer);

        var file = RecordFiles.All.Single(f => f.Product == Product);
        var sorted = file.Sorted(rows, file.Order(file.Export));
        return (sorted, new Outcome(false, sorted.Count, sorted.Select(r => r[0]).Distinct().Count(), read, withheld, fromRelationships.Count,
            gazetteer.Count));
    }

    /// <summary>The Strong numbers of the terms among <paramref name="listed"/>, each with the terms that bear it.</summary>
    private static Dictionary<string, List<int>> Terms(
        ProductStore store, List<Record> records, Dictionary<string, int> ids, IEnumerable<string> listed)
    {
        var wanted = listed.Where(slug => ids.TryGetValue(slug, out var id) && records[id].Kind == EntityKind.Term)
            .ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return [];
        }

        return Read(store, "records/names")
            .Where(r => wanted.Contains(r[0]!))
            .SelectMany(r => new[] { r[8], r[9] }.OfType<string>().Select(strong => (Strong: strong, Term: ids[r[0]!])))
            .Distinct()
            .GroupBy(p => p.Strong, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Term).ToList(), StringComparer.Ordinal);
    }

    private static bool NeedsBuild(ProductStore store, IReadOnlyList<ProductInput> inputs)
    {
        if (store.NeedsBuild(Product, ProductKind.Machine, inputs))
        {
            return true;
        }

        using var reader = store.Open(Product);
        return reader.Header?.Notes.LastOrDefault(note => note.Key == VersionNote).Value != Version;
    }

    /// <summary>The texts with a file in <c>names/&lt;folder&gt;/</c>, in name order.</summary>
    private static List<string> Texts(ProductStore store, string folder)
    {
        var path = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, NameProducts.Folder, folder);
        return Directory.Exists(path)
            ? [.. Directory.GetFiles(path).Where(file => !Path.GetFileName(file).StartsWith('.'))
                .Select(Path.GetFileNameWithoutExtension).OfType<string>().Distinct().Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>The primary frame address of every verse a text's frame places away from its own address.</summary>
    private static Dictionary<VerseKey, VerseKey> Frame(ProductStore store, string text)
    {
        var frame = new Dictionary<VerseKey, VerseKey>();
        if (!store.Exists(FrameFile.Product(text)))
        {
            return frame;
        }

        using var reader = store.Open(FrameFile.Product(text), FrameRow.Schema);
        foreach (var row in reader.Rows())
        {
            foreach (var (own, canonical) in row.Verses())
            {
                frame[own] = canonical[0];
            }
        }

        return frame;
    }

    private sealed record Record(string Slug, EntityKind Kind, string Name);

    private static List<Record> Records(ProductStore store) =>
        [.. Read(store, "records/records").Select(r => new Record(r[0]!, EnumSpelling.ToEntityKind(r[1]!), r[4]!))];

    private static List<string?[]> Read(ProductStore store, string product)
    {
        using var reader = store.Open(product, RecordFiles.All.Single(f => f.Product == product).Schema());
        return [.. reader.ReadAll()];
    }
}
