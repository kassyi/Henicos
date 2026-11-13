namespace Kassyi.GSharp.Cuda;

/// <summary>
/// Reads the <c>GSharpTargetArchitectures</c> entries as nvcc reads its architecture options. Linked into the build
/// tasks, which pass them to nvcc, and into the compiler, which takes the launch limits of the target GPUs from them.
/// </summary>
/// <remarks>
/// The entries are separated by <c>;</c>; empty entries are skipped. An entry is one of:
/// <list type="bullet">
///   <item><description>
///     <c>compute_XY,sm_XY</c> / <c>compute_XY,compute_XY</c>: one <c>-gencode</c> option with that arch and code.
///   </description></item>
///   <item><description>
///     <c>sm_XY</c>: SASS for sm_XY and PTX for compute_XY, as <c>nvcc -arch=sm_XY</c>.
///   </description></item>
///   <item><description>
///     <c>compute_XY</c>: PTX for compute_XY, as <c>nvcc -arch=compute_XY</c>.
///   </description></item>
/// </list>
/// <c>XY</c> is two or more digits, optionally followed by <c>a</c> or <c>f</c>. Any other entry is invalid.
/// Each caller collects the options in a collection of its own (the compiler bans <c>List&lt;T&gt;</c>).
/// </remarks>
internal static class CudaArchitectureList
{
    /// <summary>
    /// The architectures compiled when <c>GSharpTargetArchitectures</c> is not set: sm_86 SASS, and compute_86 PTX
    /// that the driver JIT-compiles on GPUs the SASS cannot run on (compute capability 9.0 and newer).
    /// </summary>
    public const string DefaultTargetArchitectures = "compute_86,sm_86;compute_86,compute_86";

    /// <summary>The prefix of a virtual architecture (PTX).</summary>
    public const string VirtualPrefix = "compute_";

    /// <summary>The prefix of a real architecture (SASS).</summary>
    public const string RealPrefix = "sm_";

    /// <summary>
    /// Takes the next non-empty entry, trimmed, from the front of <paramref name="rest"/>; returns
    /// <see langword="false"/> when no entry is left.
    /// </summary>
    public static bool TryReadEntry(ref ReadOnlySpan<char> rest, out ReadOnlySpan<char> entry)
    {
        while (rest.Length > 0)
        {
            var separator = rest.IndexOf(';');
            entry = (separator < 0 ? rest : rest.Slice(0, separator)).Trim();
            rest = separator < 0 ? ReadOnlySpan<char>.Empty : rest.Slice(separator + 1);
            if (entry.Length > 0)
            {
                return true;
            }
        }

        entry = ReadOnlySpan<char>.Empty;
        return false;
    }

    /// <summary>
    /// Turns one entry into its <c>-gencode</c> options: returns how many it stands for (1 or 2, written to
    /// <paramref name="first"/> and then <paramref name="second"/>), or 0 when the entry is invalid.
    /// </summary>
    public static int ReadGencodes(ReadOnlySpan<char> entry, out NvccGencode first, out NvccGencode second)
    {
        first = default;
        second = default;
        var commaIndex = entry.IndexOf(',');
        if (commaIndex >= 0)
        {
            var arch = entry.Slice(0, commaIndex).Trim();
            var code = entry.Slice(commaIndex + 1).Trim();
            if (!IsArchitecture(arch, VirtualPrefix) ||
                !(IsArchitecture(code, RealPrefix) || IsArchitecture(code, VirtualPrefix)))
            {
                return 0;
            }

            first = new NvccGencode(arch.ToString(), code.ToString());
            return 1;
        }

        if (IsArchitecture(entry, VirtualPrefix))
        {
            var text = entry.ToString();
            first = new NvccGencode(text, text);
            return 1;
        }

        if (!IsArchitecture(entry, RealPrefix))
        {
            return 0;
        }

        // nvcc -arch=sm_XY means -gencode arch=compute_XY,code=[sm_XY,compute_XY]; the suffix stays on both.
        var virtualArch = VirtualPrefix + entry.Slice(RealPrefix.Length).ToString();
        first = new NvccGencode(virtualArch, entry.ToString());
        second = new NvccGencode(virtualArch, virtualArch);
        return 2;
    }

    /// <summary>Whether <paramref name="text"/> is <paramref name="prefix"/>, two or more digits and an optional
    /// <c>a</c> or <c>f</c>.</summary>
    public static bool IsArchitecture(ReadOnlySpan<char> text, string prefix)
    {
        if (!text.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
        {
            return false;
        }

        var end = text.Length;
        if (end > prefix.Length && text[end - 1] is 'a' or 'f')
        {
            end--;
        }

        var digitCount = end - prefix.Length;
        if (digitCount < 2)
        {
            return false;
        }

        for (var i = prefix.Length; i < end; i++)
        {
            if (text[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
