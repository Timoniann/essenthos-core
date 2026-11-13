using Essenthos.Core.Build;
using Essenthos.Core.Conversion;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Integrity;

/// <summary>
/// The build refuses whatever it may not serve (PRB-1545): a source a product reads whose licence grants nothing, one nobody
/// declared, one kept only to measure against, a text whose declaration forbids serving it, a picture without its licence or
/// credit; a source the owner keeps although nothing grants it is reported as an exception instead.
/// </summary>
public sealed class SourceLicencesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    private static SourceLicence Row(string source, string redistribution, string served = SourceLicences.Content, string layer = "text",
        string[]? exception = null, string[]? credit = null) =>
        new(source, layer, served, "a licence", redistribution, credit ?? (served == SourceLicences.Never ? [] : ["dataset:openbible"]), "-",
            exception ?? [], "-");

    private static SourceLicences Licences(params SourceLicence[] rows) => new(rows);

    [Fact]
    public void A_source_whose_licence_prohibits_redistribution_is_refused()
    {
        var found = Licences(Row("Locked", SourceLicences.Prohibited)).Read("texts/X/words", "Locked/book.xml").ToList();

        found.Should().ContainSingle().Which.Should().Match<LicenceFinding>(f => !f.Excepted && f.Why.Contains("prohibited"));
    }

    [Fact]
    public void A_source_whose_licence_is_unknown_is_refused()
    {
        var found = Licences(Row("Silent", SourceLicences.Unknown)).Read("mapping/A-B/strong", "Silent").ToList();

        found.Should().ContainSingle().Which.Excepted.Should().BeFalse();
    }

    [Fact]
    public void A_source_nobody_declared_is_refused()
    {
        Licences(Row("Open", "public-domain")).Read("texts/X/words", "Elsewhere/file.txt").Should().ContainSingle()
            .Which.Why.Should().Contain("declares the source");
    }

    [Fact]
    public void A_source_is_held_to_its_longest_declared_path()
    {
        var licences = Licences(Row("Lexicons", "public-domain"), Row("Lexicons/Quarantined.txt", SourceLicences.Prohibited, SourceLicences.Never));

        licences.Read("tables/x", "Lexicons/Brief.txt").Should().BeEmpty();
        licences.Read("tables/x", "Lexicons/Quarantined.txt").Should().ContainSingle();
        licences.For("Lexicons/Quarantined.txt/inner").Single().Source.Should().Be("Lexicons/Quarantined.txt");
        licences.For("LexiconsElse").Should().BeEmpty("a source is matched by whole segments");
    }

    [Fact]
    public void What_is_kept_to_measure_against_is_refused_on_the_served_path_and_allowed_off_it()
    {
        var licences = Licences(Row("Measured", SourceLicences.Prohibited, SourceLicences.Never), Row("Open", "public-domain"));
        var manifest = Manifest(("Essenthos/texts/X/words.essx", ["Open"]));

        licences.CheckManifest(manifest).Should().BeEmpty("nothing the build makes reads the measured source");

        manifest = Manifest(("Essenthos/texts/X/words.essx", ["Open", "Measured/text.epub"]));
        licences.CheckManifest(manifest).Should().ContainSingle().Which.Should().Match<LicenceFinding>(f => !f.Excepted && f.Why.Contains("never"));
    }

    [Fact]
    public void The_New_World_Translation_is_measured_and_never_served()
    {
        var licences = SourceLicences.Read(new ProductStore(TestResources.Shipped(".")));

        licences.For(NewWorldTextSource.Folder).Should().ContainSingle()
            .Which.Should().Match<SourceLicence>(l => l.Served == SourceLicences.Never && l.Redistribution == SourceLicences.Prohibited);
        TextCatalogue.All.Should().NotContain(t => t.Slug == NewWorldTextSource.Slug);
        licences.CheckManifest(Manifest(("Essenthos/texts/KJV/words.essx", [$"{NewWorldTextSource.Folder}/nwt_E.epub"])))
            .Should().ContainSingle().Which.Excepted.Should().BeFalse();
    }

    [Fact]
    public void A_text_whose_definition_forbids_serving_it_is_never_converted()
    {
        var store = new ProductStore(_root);
        var measured = new CataloguedText(NewWorldTextSource.Slug, [], (_, _) => new TextSource(NewWorldTextSource.Definition, []));

        FluentActions.Invoking(() => TextConverter.Convert(store, measured)).Should().Throw<InvalidOperationException>()
            .WithMessage("*never written to the corpus*");
        store.Exists(TextField.ProductOf(NewWorldTextSource.Slug)).Should().BeFalse();
        store.Exists(TextWord.ProductOf(NewWorldTextSource.Slug)).Should().BeFalse();
    }

    [Fact]
    public void A_source_the_owner_keeps_although_nothing_grants_it_is_an_exception_not_a_refusal()
    {
        var found = Licences(Row("Tags", SourceLicences.Unknown, layer: "strong", exception: ["PRB-1332"])).Read("texts/KJV/words", "Tags/kjv.xml").ToList();

        found.Should().ContainSingle().Which.Should().Match<LicenceFinding>(f => f.Excepted && f.Why.Contains("PRB-1332"));
    }

    [Fact]
    public void A_layer_the_reader_refuses_beside_a_served_one_is_not_held_against_the_source()
    {
        var licences = Licences(Row("Bible", "public-domain"), Row("Bible", SourceLicences.Unknown, SourceLicences.Never, layer: "strong"));

        licences.Read("texts/X/words", "Bible").Should().BeEmpty();
    }

    [Fact]
    public void A_declaration_that_cannot_be_held_to_anything_is_refused_when_read()
    {
        FluentActions.Invoking(() => Licences(Row("Open", "public-domain", exception: ["PRB-1332"])))
            .Should().Throw<InvalidDataException>().WithMessage("*exception*");
        FluentActions.Invoking(() => Licences(Row("Open", "public-domain", credit: ["dataset:nobody"])))
            .Should().Throw<InvalidDataException>().WithMessage("*no dataset*");
        FluentActions.Invoking(() => Licences(Row("Open", "public-domain", credit: [])))
            .Should().Throw<InvalidDataException>().WithMessage("*RUL-0181*");
        FluentActions.Invoking(() => Licences(Row("Open", "free")))
            .Should().Throw<InvalidDataException>().WithMessage("*redistribution*");
        FluentActions.Invoking(() => Licences(Row("Open", "public-domain"), Row("Open", "public-domain")))
            .Should().Throw<InvalidDataException>().WithMessage("*twice*");
    }

    [Fact]
    public void A_datasets_rows_kept_in_the_store_are_held_to_the_datasets_licence()
    {
        var licences = Licences(Row("Essenthos/external/open", "public-domain"));

        licences.CheckManifest(Manifest(("Essenthos/external/open/records.tsv", []), ("Essenthos/external/closed/records.tsv", [])))
            .Should().ContainSingle().Which.Subject.Should().Contain("external/closed");
    }

    [Fact]
    public void A_texts_declaration_is_held_to_what_the_definition_is()
    {
        var store = new ProductStore(_root);
        Declare(store, "KJV", "prohibited");
        Declare(store, "BSB", "unknown");
        Declare(store, "ASV", "public-domain");

        SourceLicences.CheckTexts(store).Select(f => f.Subject).Should().BeEquivalentTo(TextField.ProductOf("KJV"), TextField.ProductOf("BSB"));
    }

    [Fact]
    public void A_picture_without_its_licence_or_credit_is_refused()
    {
        var store = new ProductStore(_root);
        Pictures(store,
            Picture("abraham", "public", "a.jpg", "Commons", "CC BY-SA 4.0"),
            Picture("sarah", "public", "b.jpg", "Commons", ""),
            Picture("isaac", "public", "c.jpg", "", "Public domain"),
            Picture("jacob", "public", "d.jpg", "Commons", "All rights reserved"),
            Picture("eden", "generated", "e.webp", "Essenthos", "Essenthos's own work"),
            Picture("ark", "public", "f.webp", "Essenthos", "Essenthos's own work"));

        SourceLicences.CheckPictures(store).Select(f => f.Subject.Split(": ")[1]).Should()
            .BeEquivalentTo("b.jpg of sarah", "c.jpg of isaac", "d.jpg of jacob", "f.webp of ark");
    }

    [Fact]
    public void The_checked_in_licences_declare_every_source_the_checked_in_graph_reads_and_except_only_what_the_owner_keeps()
    {
        var store = new ProductStore(TestResources.Shipped("."));
        var licences = SourceLicences.Read(store);

        var found = licences.CheckDeclared(ProductGraph.Read(store));

        found.Where(f => !f.Excepted).Should().BeEmpty();
        found.Select(f => f.Subject).Should().BeEquivalentTo(
            "Zefania (strong)", "Luther1912 (strong)", "ReinaValera1909 (strong)", "Segond1910Strong (strong)", "DarbyFrench (strong)",
            "SynodalStrong (strong)");
    }

    [Fact]
    public void Every_source_with_a_licence_file_is_declared_and_every_declared_reading_is_there()
    {
        var resources = TestResources.Shipped(".");
        var licences = SourceLicences.Read(new ProductStore(resources));

        foreach (var folder in Directory.EnumerateDirectories(resources).Select(Path.GetFileName).Where(f => f != ProductStore.EssenthosFolder))
        {
            if (File.Exists(Path.Combine(resources, folder!, "LICENCE.md")))
            {
                licences.Rows.Should().Contain(r => r.Source == folder || r.Source.StartsWith(folder + "/"), $"{folder} has a LICENCE.md");
            }
        }

        foreach (var row in licences.Rows.Where(r => r.Declared != "-"))
        {
            File.Exists(Path.Combine(resources, row.Declared)).Should().BeTrue($"{row} says its licence was read in {row.Declared}");
        }
    }

    private static List<ManifestEntry> Manifest(params (string Product, string[] Inputs)[] entries) =>
    [
        .. entries.Select(e => new ManifestEntry(e.Product, new string('0', 64), 1, DateTimeOffset.UnixEpoch, "test",
            [.. e.Inputs.Select(i => new ProductInput(i, new string('1', 64)))])),
    ];

    private static void Declare(ProductStore store, string slug, string redistribution)
    {
        using var writer = store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []);
        writer.Write(new TextField("source-url", "https://example.org"));
        writer.Write(new TextField("licence", "a licence"));
        writer.Write(new TextField("redistribution", redistribution));
        writer.Complete();
    }

    private static string?[] Picture(string slug, string kind, string file, string credit, string licence)
    {
        var columns = RecordProducers.File("records/facts/images").Parsed.Select(c => c.Name).ToList();
        var row = new string?[columns.Count];
        row[columns.IndexOf("slug")] = slug;
        row[columns.IndexOf("kind")] = kind;
        row[columns.IndexOf("role")] = "primary";
        row[columns.IndexOf("ordinal")] = "1";
        row[columns.IndexOf("file")] = file;
        row[columns.IndexOf("digest")] = "0";
        row[columns.IndexOf("width")] = "1";
        row[columns.IndexOf("height")] = "1";
        row[columns.IndexOf("credit")] = credit;
        row[columns.IndexOf("licence")] = licence;
        row[columns.IndexOf("source")] = "test";
        return row;
    }

    private static void Pictures(ProductStore store, params string?[][] rows) =>
        RecordProducers.Write(store, RecordProducers.File("records/facts/images"), rows, "test", []);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
