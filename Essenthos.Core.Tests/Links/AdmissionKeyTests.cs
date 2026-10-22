using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// What a composition may be measured on. The Almeida's italics state 16,000 of its words as the
/// translator's own, rendering no Hebrew word; measured on them alone, every answer the readings give
/// is wrong, and every combination of readings was refused at 0 %.
/// </summary>
public class AdmissionKeyTests
{
    [Fact]
    public void A_key_that_states_only_words_rendering_nothing_measures_nothing()
    {
        var absences = Enumerable.Range(1, 5_000).ToDictionary(word => (long)word, _ => new HashSet<long>());
        var answers = Enumerable.Range(1, 5_000).Select(word => new RoutedLink(word, 90_000 + word, 0.9, Route.Written)).ToList();

        Admission.Measure(answers, absences, 0.25).Floors[Route.Written].Refused.Should().BeTrue(
            "read off absences alone, every answer is wrong whatever the readings did");
        Admission.Measures(absences).Should().BeFalse();
    }

    [Fact]
    public void A_key_measures_once_enough_of_its_words_render_a_word()
    {
        var key = Enumerable.Range(1, 5_000).ToDictionary(word => (long)word, _ => new HashSet<long>());
        for (var word = 1; word < Admission.FewestToMeasure; word++)
        {
            key[word].Add(90_000 + word);
        }

        Admission.Measures(key).Should().BeFalse();

        key[Admission.FewestToMeasure].Add(90_000 + Admission.FewestToMeasure);
        Admission.Measures(key).Should().BeTrue();
    }
}
