using MixedMediaPrint.Core.Execution;
using MixedMediaPrint.Core.Printing.Gdi;
using Xunit;

namespace MixedMediaPrint.Tests.Execution;

public class TrayResolverTests
{
    private static readonly CapabilityOption[] Bins =
    [
        new(1, "Tray 1"),
        new(2, "Tray 2"),
        new(4, "Bypass Tray"),
    ];

    [Fact]
    public void Resolve_ExactlyOneMatch_ReturnsIt()
    {
        CapabilityOption result = TrayResolver.Resolve(Bins, "(?i)tray\\s*1");

        Assert.Equal(1, result.Id);
    }

    [Fact]
    public void Resolve_IsCaseInsensitiveWhenPatternSays_UsesGivenPatternExactly()
    {
        // Pattern itself carries the (?i) flag, matching how the original scripts
        // always wrote their patterns — TrayResolver doesn't add its own casing rules.
        CapabilityOption result = TrayResolver.Resolve(Bins, "(?i)bypass");

        Assert.Equal(4, result.Id);
    }

    [Fact]
    public void Resolve_NoMatch_ThrowsWithAvailableNamesListed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TrayResolver.Resolve(Bins, "(?i)tray\\s*99"));

        Assert.Contains("Tray 1", ex.Message);
        Assert.Contains("Bypass Tray", ex.Message);
    }

    [Fact]
    public void Resolve_AmbiguousMatch_Throws()
    {
        // "(?i)tray" alone matches both "Tray 1" and "Tray 2" -- must fail closed,
        // not silently pick the first hit the way the original scripts' plain
        // `-match` did.
        var ex = Assert.Throws<InvalidOperationException>(() => TrayResolver.Resolve(Bins, "(?i)tray"));

        Assert.Contains("multiple bins", ex.Message);
    }
}
