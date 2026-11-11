namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// Specifies the native C++ sanitizers to enable when compiling host shadow kernels.
/// </summary>
[Flags]
public enum NativeSanitizers
{
    /// <summary>No native sanitizers enabled.</summary>
    None = 0,

    /// <summary>AddressSanitizer (<c>-fsanitize=address</c> on GCC/Clang, <c>/fsanitize=address</c> on MSVC).</summary>
    Address = 1 << 0,

    /// <summary>UndefinedBehaviorSanitizer (<c>-fsanitize=undefined</c> on GCC/Clang).</summary>
    Undefined = 1 << 1
}

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

        if (string.IsNullOrWhiteSpace(value))
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

            var name = TrimmedPart(value, start, end);
            start = end + 1;
            if (name.Length == 0)
            {
                continue;
            }

            if (string.Equals(name, "address", StringComparison.OrdinalIgnoreCase))
            {
                sanitizers |= NativeSanitizers.Address;
            }
            else if (string.Equals(name, "undefined", StringComparison.OrdinalIgnoreCase))
            {
                sanitizers |= NativeSanitizers.Undefined;
            }
            else
            {
                sanitizers = NativeSanitizers.None;
                invalidToken = name;
                return false;
            }
        }

        return true;
    }

    // <value>[start..end] without the white space at either end
    private static string TrimmedPart(string value, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }
}
