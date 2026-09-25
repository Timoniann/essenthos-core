using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The record of Forge runs a load replays: what is recorded, that a run repeated moves to the end
/// instead of appearing twice, and that the committed recipe reads.
/// </summary>
public sealed class RecipeTests : IDisposable
{
    private static readonly DateTimeOffset First = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    private readonly string _resources = Directory.CreateTempSubdirectory("essenthos-recipe").FullName;

    public void Dispose() => Directory.Delete(_resources, recursive: true);

    [Theory]
    [InlineData(new[] { "align", "BSB", "KJV", "--replace" }, true)]
    [InlineData(new[] { "compose", "ELB1905", "KJV,BSB", "BHSA" }, true)]
    [InlineData(new[] { "compose", "ELB1905", "KJV,BSB", "BHSA", "--dry-run" }, false)]
    [InlineData(new[] { "names", "ASV", "BHSA" }, false)]
    [InlineData(new[] { "names", "ASV", "BHSA", "--apply" }, true)]
    [InlineData(new[] { "interlinear-join", "UBIO" }, false)]
    [InlineData(new[] { "interlinear-join", "UBIO", "--replace" }, true)]
    [InlineData(new[] { "correct" }, true)]
    [InlineData(new[] { "verify" }, false)]
    [InlineData(new[] { "carry" }, false)]
    public void Only_a_run_that_wrote_what_the_load_does_not_is_recorded(string[] args, bool recorded) =>
        Recipe.Records(args).Should().Be(recorded);

    [Fact]
    public void A_run_repeated_moves_to_the_end_and_is_not_written_twice()
    {
        Recipe.Record(_resources, ["align", "GEEZ81", "SWETE", "--replace"], First);
        Recipe.Record(_resources, ["compose", "GEEZ81", "GRCBRENT", "BHSA", "--daughter"], First.AddMinutes(1));
        Recipe.Record(_resources, ["align", "geez81", "swete", "--replace"], First.AddMinutes(2));
        Recipe.Record(_resources, ["compose", "GEEZ81", "GRCBRENT", "BHSA", "--dry-run"], First.AddMinutes(3));

        var steps = Recipe.Read(_resources);
        steps.Select(step => step.ToString()).Should().Equal(
            "compose GEEZ81 GRCBRENT BHSA --daughter",
            "align geez81 swete --replace");
        steps[^1].At.Should().Be(First.AddMinutes(2));
    }

    [Fact]
    public void A_configuration_switch_is_not_part_of_the_step()
    {
        Recipe.Record(_resources, ["correct", "--Database:ConnectionString=Host=elsewhere"], First);

        Recipe.Read(_resources).Single().Arguments.Should().BeEmpty();
    }

    [Fact]
    public void The_committed_recipe_reads_and_every_step_is_a_verb_that_records_itself()
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(checkout.FullName, "Essenthos.Forge")))
        {
            checkout = checkout.Parent!;
        }

        var steps = Recipe.Read(Path.Combine(checkout.FullName, "Resources"));

        steps.Should().NotBeEmpty();
        steps.Should().OnlyContain(step => Recipe.Records(new[] { step.Verb }.Concat(step.Arguments).ToArray()));
    }
}
