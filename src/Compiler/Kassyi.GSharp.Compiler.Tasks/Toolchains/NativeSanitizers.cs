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

        var span = value.AsSpan();
        while (span.Length > 0)
        {
            var semiIdx = span.IndexOf(';');
            var commaIdx = span.IndexOf(',');
            var sepIdx = semiIdx >= 0 && commaIdx >= 0
                ? Math.Min(semiIdx, commaIdx)
                : Math.Max(semiIdx, commaIdx);

            var segment = (sepIdx >= 0 ? span.Slice(0, sepIdx) : span).Trim();
            if (segment.Length > 0)
            {
                if (segment.Equals("address".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    sanitizers |= NativeSanitizers.Address;
                }
                else if (segment.Equals("undefined".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    sanitizers |= NativeSanitizers.Undefined;
                }
                else
                {
                    sanitizers = NativeSanitizers.None;
                    invalidToken = segment.ToString();
                    return false;
                }
            }

            if (sepIdx < 0)
            {
                break;
            }

            span = span.Slice(sepIdx + 1);
        }

        return true;
    }
}
