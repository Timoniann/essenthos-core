using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// The title row a psalm's first verse covers is written after the frame, by the passes that read the
/// title in, and the frame does not count it as a difference of its own.
/// </summary>
public class FramePlacementComparisonTests
{
    private static readonly ReferenceDraft[] Framed =
    [
        new(1, 19, 3, 1, true),
        new(2, 19, 3, 2, true),
    ];

    [Fact]
    public void ATitleRowCoveredAfterTheFrameIsNotADifference()
    {
        CanonicalFrameLoader.Unchanged([.. Framed, new(1, 19, 3, 0, false)], Framed).Should().BeTrue();
    }

    [Fact]
    public void AnyOtherRowIsADifference()
    {
        CanonicalFrameLoader.Unchanged([.. Framed, new(1, 19, 3, 2, false)], Framed).Should().BeFalse();
        CanonicalFrameLoader.Unchanged([Framed[0]], Framed).Should().BeFalse();
        CanonicalFrameLoader.Unchanged([Framed[0], new(2, 19, 3, 0, true)], Framed).Should().BeFalse();
    }
}
