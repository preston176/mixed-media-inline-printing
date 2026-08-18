using System.Text.RegularExpressions;
using MixedMediaPrint.Core.Printing.Gdi;

namespace MixedMediaPrint.Core.Execution;

/// <summary>
/// Resolves a human-friendly tray pattern (e.g. "(?i)tray\s*1") to the driver's
/// exact bin, against the printer's own live capability list — never a
/// hardcoded id. Fail-closed: carries forward legacy-testkit/ScenarioResolver.psm1's
/// design philosophy (never guess; abort loudly on zero or ambiguous matches)
/// with one addition — the original scripts only checked for zero matches via
/// first-hit `-match`; this also rejects multiple matches rather than silently
/// picking the first one.
///
/// Pure — operates on an already-fetched capability list, no OS dependency.
/// </summary>
public static class TrayResolver
{
    public static CapabilityOption Resolve(IReadOnlyList<CapabilityOption> bins, string pattern)
    {
        var regex = new Regex(pattern);
        List<CapabilityOption> matches = bins.Where(b => regex.IsMatch(b.Name)).ToList();

        if (matches.Count == 0)
        {
            string available = string.Join(", ", bins.Select(b => $"'{b.Name}'"));
            throw new InvalidOperationException($"No bin matching pattern '{pattern}' found. Available: {available}.");
        }
        if (matches.Count > 1)
        {
            string matched = string.Join(", ", matches.Select(b => $"'{b.Name}'"));
            throw new InvalidOperationException($"Pattern '{pattern}' matched multiple bins ({matched}); make the pattern more specific.");
        }

        return matches[0];
    }
}
