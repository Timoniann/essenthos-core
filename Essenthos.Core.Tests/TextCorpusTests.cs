using System.Collections;
using System.Reflection;
using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The list of every text is declared once, and this is what keeps it honest.
///
/// Two test classes used to keep their own copies and one of them had already fallen behind: the
/// Samaritan Pentateuch was loading and the checks that a text answers to its other identifiers
/// silently stopped covering it. Moving them onto one list fixes that pair and not the cause —
/// whoever adds the twelfth text still has to remember. So instead of asking a person to remember,
/// this asks the assembly: every <see cref="TextDefinition"/> any text source can hand out must be
/// in <see cref="TextCorpus.Definitions"/>, and a new source that is not declared there fails here
/// with its own slug in the message.
/// </summary>
public sealed class TextCorpusTests
{
    [Fact]
    public void EveryTextIsDeclaredOnce() =>
        TextCorpus.Slugs.Should().OnlyHaveUniqueItems();

    /// <summary>
    /// Found by reflection rather than listed, because a list is the thing that goes stale. Every
    /// static member of a text source that yields a definition is called: a property, a
    /// parameterless method, a method taking one enum (which is how the two Textus Receptus
    /// editions are told apart), and a collection of them.
    /// </summary>
    [Fact]
    public void NoTextSourceOffersATextTheCorpusHasNotHeardOf()
    {
        var declared = TextCorpus.Slugs.ToHashSet(StringComparer.Ordinal);
        var offered = Offered().ToHashSet(StringComparer.Ordinal);

        offered.Should().NotBeEmpty(
            "reflection found no definitions at all, so this check passes without checking "
            + "anything — the naming or the shape it looks for has moved");
        offered.Except(declared).Should().BeEmpty(
            "a text source hands out a definition TextCorpus.Definitions does not list, so every "
            + "check written against that list — provenance, licence, aliases — silently skips it");
        declared.Except(offered).Should().BeEmpty(
            "TextCorpus.Definitions lists a text no source can produce, so the list has outlived a "
            + "source that was removed");
    }

    private static IEnumerable<string> Offered()
    {
        var sources = typeof(TextCorpus).Assembly.GetTypes()
            .Where(type => type.Name.EndsWith("TextSource", StringComparison.Ordinal));

        foreach (var member in sources.SelectMany(type =>
                     type.GetMembers(BindingFlags.Public | BindingFlags.Static)))
        {
            foreach (var slug in Yielded(member))
            {
                yield return slug;
            }
        }
    }

    private static IEnumerable<string> Yielded(MemberInfo member)
    {
        switch (member)
        {
            case FieldInfo field:
                return Slugs(field.GetValue(null));
            case PropertyInfo property:
                return Slugs(property.GetValue(null));
            case MethodInfo method when !method.IsSpecialName:
                return Invoked(method);
            default:
                return [];
        }
    }

    /// <summary>
    /// A method is only called where every argument can be supplied without reading a file: no
    /// parameters, or one enum, which is enumerated. Anything else takes a parsed text and cannot
    /// be a declaration.
    /// </summary>
    private static IEnumerable<string> Invoked(MethodInfo method)
    {
        if (method.ReturnType != typeof(TextDefinition))
        {
            return [];
        }

        var parameters = method.GetParameters();
        return parameters switch
        {
            [] => Slugs(method.Invoke(null, null)),
            [{ ParameterType.IsEnum: true } only] =>
                Enum.GetValues(only.ParameterType).Cast<object>()
                    .SelectMany(value => Slugs(method.Invoke(null, [value]))),
            _ => [],
        };
    }

    private static IEnumerable<string> Slugs(object? value) => value switch
    {
        TextDefinition definition => [definition.Slug],
        IDictionary map => map.Values.Cast<object?>().SelectMany(Slugs),
        IEnumerable many and not string => many.Cast<object?>().SelectMany(Slugs),
        _ => [],
    };
}
