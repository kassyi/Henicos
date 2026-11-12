namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>The paths a <c>fuzz-diff run</c> or <c>fuzz-diff compare</c> builds and runs the batches on.</summary>
public enum FuzzProfile
{
    /// <summary>The managed path only (<c>M</c>).</summary>
    Managed,

    /// <summary>The managed and native CPU paths (<c>C</c>).</summary>
    Cpu,

    /// <summary>The managed, native and CUDA paths (<c>G</c>).</summary>
    All,
}

/// <summary>
/// Converts <see cref="FuzzProfile"/> to and from the one-letter codes (<c>M</c>, <c>C</c>, <c>G</c>) that the
/// generator options, <c>known-issues.json</c>, the child commands and <c>summary.json</c> use.
/// </summary>
internal static class FuzzProfileCodes
{
    /// <summary>Returns the one-letter code of <paramref name="profile"/>.</summary>
    internal static string ToCode(this FuzzProfile profile) =>
        profile switch
        {
            FuzzProfile.Managed => "M",
            FuzzProfile.All => "G",
            _ => "C",
        };

    /// <summary>Reads a code returned by <see cref="KnownIssueFilter.NormalizeProfile"/>.</summary>
    internal static FuzzProfile FromCode(string code) =>
        code switch
        {
            "M" => FuzzProfile.Managed,
            "G" => FuzzProfile.All,
            _ => FuzzProfile.Cpu,
        };

    /// <summary>
    /// Reads the <c>--profile</c> value of <c>fuzz-diff run</c>: a code or a name, in any case; anything else is
    /// rejected.
    /// </summary>
    internal static bool TryParseStrict(string raw, out FuzzProfile profile)
    {
        ReadOnlySpan<char> text = raw.AsSpan().Trim();
        FuzzProfile? parsed =
            IsAnyOf(text, "m", "managed") ? FuzzProfile.Managed
            : IsAnyOf(text, "c", "cpu", "native") ? FuzzProfile.Cpu
            : IsAnyOf(text, "g", "all", "cuda", "gpu") ? FuzzProfile.All
            : null;
        profile = parsed ?? FuzzProfile.Cpu;
        return parsed is not null;
    }

    private static bool IsAnyOf(ReadOnlySpan<char> text, params ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            if (text.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
