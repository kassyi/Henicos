using System.Collections.Immutable;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// A set of globs matched against repository-relative paths with <c>/</c> separators. It uses the matcher of
/// <c>Microsoft.Extensions.FileSystemGlobbing</c> (<c>*</c>, <c>**</c>, <c>?</c>; no <c>{a,b}</c> alternatives).
/// An empty set matches nothing.
/// </summary>
internal sealed class GlobSet
{
    private readonly Matcher? _matcher;

    /// <summary>Creates a set that matches a path when any of <paramref name="patterns"/> does.</summary>
    public GlobSet(ImmutableArray<string> patterns)
    {
        if (patterns.IsDefaultOrEmpty)
        {
            return;
        }

        _matcher = new Matcher(StringComparison.Ordinal);
        foreach (var pattern in patterns)
        {
            _matcher.AddInclude(pattern);
        }
    }

    /// <summary>Returns true when <paramref name="path"/> matches any glob of the set.</summary>
    public bool IsMatch(string path) => _matcher is not null && _matcher.Match("/", path).HasMatches;
}
