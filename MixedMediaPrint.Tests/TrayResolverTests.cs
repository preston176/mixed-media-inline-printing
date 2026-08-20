using MixedMediaPrint.Core;
using MixedMediaPrint.Core.Printing;

namespace MixedMediaPrint.Tests;

public class TrayResolverTests
{
    private static readonly PrinterBin[] Bins =
    {
        new(1, "Tray 1"),
        new(2, "Tray 2"),
        new(4, "Bypass Tray"),
    };

    [Fact]
    public void Resolve_DefaultPatterns_ResolvesTray1AsTabAndTray2AsBody()
    {
        var result = TrayResolver.Resolve(Bins, tabTrayPattern: @"(?i)tray\s*1", bodyTrayPattern: @"(?i)tray\s*2");

        Assert.Equal(1, result.TabTrayId);
        Assert.Equal(2, result.BodyTrayId);
    }

    [Fact]
    public void Resolve_BypassScenario_ResolvesBypassAsTabAndTray1AsBody()
    {
        var result = TrayResolver.Resolve(Bins, tabTrayPattern: "(?i)bypass", bodyTrayPattern: @"(?i)tray\s*1");

        Assert.Equal(4, result.TabTrayId);
        Assert.Equal(1, result.BodyTrayId);
    }

    [Fact]
    public void Resolve_NoMatchForBodyPattern_ThrowsWithThePatternInTheMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TrayResolver.Resolve(Bins, tabTrayPattern: @"(?i)tray\s*1", bodyTrayPattern: "(?i)nonexistent"));

        Assert.Contains("nonexistent", ex.Message);
    }

    [Fact]
    public void Resolve_NoMatchForTabPattern_ThrowsWithThePatternInTheMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TrayResolver.Resolve(Bins, tabTrayPattern: "(?i)nonexistent", bodyTrayPattern: @"(?i)tray\s*2"));

        Assert.Contains("nonexistent", ex.Message);
    }

    [Fact]
    public void Resolve_BothPatternsMatchTheSameBin_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TrayResolver.Resolve(Bins, tabTrayPattern: @"(?i)tray\s*1", bodyTrayPattern: @"(?i)tray\s*1"));

        Assert.Contains("same bin", ex.Message);
    }
}
