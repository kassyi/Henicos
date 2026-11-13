namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// Parses the <c>GSharpNativeSanitizers</c> MSBuild property into <see cref="NativeSanitizers"/> flags.
/// </summary>
public static class NativeSanitizerParser
{
    private static readonly char[] s_separators = [';', ','];

    /// <summary>
    /// Parses a semicolon- or comma-separated list of sanitizer names (<c>address</c>, <c>undefined</c>).
    /// </summary>
    /// <param name="value">The raw MSBuild property value.</param>
    /// <param name="sanitizers">The parsed <see cref="NativeSanitizers"/> flags when successful.</param>
    /// <param name="invalidToken">The first unrecognized token when parsing fails; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is empty or contains only <c>address</c> and <c>undefined</c>; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out NativeSanitizers sanitizers, out string? invalidToken)
    {
        sanitizers = NativeSanitizers.None;
        invalidToken = null;

        if (value is null || string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        for (var start = 0; start <= value.Length;)
        {
            var end = value.IndexOfAny(s_separators, start);
            if (end < 0)
            {
                end = value.Length;
            }

            var name = value.AsSpan(start, end - start).Trim();
            start = end + 1;
            if (name.Length == 0)
            {
                continue;
            }

            if (!TryParseName(name, out var sanitizer))
            {
                sanitizers = NativeSanitizers.None;
                invalidToken = name.ToString();
                return false;
            }

            sanitizers |= sanitizer;
        }

        return true;
    }

    // One name of the list, address or undefined in any case
    private static bool TryParseName(ReadOnlySpan<char> name, out NativeSanitizers sanitizer)
    {
        sanitizer = name.Equals("address".AsSpan(), StringComparison.OrdinalIgnoreCase) ? NativeSanitizers.Address
            : name.Equals("undefined".AsSpan(), StringComparison.OrdinalIgnoreCase) ? NativeSanitizers.Undefined
            : NativeSanitizers.None;
        return sanitizer != NativeSanitizers.None;
    }
}
