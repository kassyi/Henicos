namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>Reads the <c>GSharpTargetArchitectures</c> entries as nvcc reads its architecture options.</summary>
internal static class NvccArchitectures
{
    /// <summary>The MSBuild error code for an entry that is not a GPU architecture nvcc accepts.</summary>
    public const string InvalidEntryErrorCode = "GS0004";

    private const string VirtualPrefix = "compute_";
    private const string RealPrefix = "sm_";

    /// <summary>
    /// Turns each entry into its <c>-gencode</c> options:
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>compute_XY,sm_XY</c> / <c>compute_XY,compute_XY</c>: one option with that arch and code.
    ///   </description></item>
    ///   <item><description>
    ///     <c>sm_XY</c>: SASS for sm_XY and PTX for compute_XY, as <c>nvcc -arch=sm_XY</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <c>compute_XY</c>: PTX for compute_XY, as <c>nvcc -arch=compute_XY</c>.
    ///   </description></item>
    /// </list>
    /// <c>XY</c> is two or more digits, optionally followed by <c>a</c> or <c>f</c>. Any other entry is invalid.
    /// </summary>
    /// <param name="targetArchitectures">The entries separated by <c>;</c>; empty entries are skipped.</param>
    public static NvccArchitecturePlan Parse(string targetArchitectures)
    {
        var gencodes = new List<NvccGencode>();
        var invalidEntries = new List<string>();
        var rest = (targetArchitectures ?? "").AsSpan();
        while (rest.Length > 0)
        {
            var separator = rest.IndexOf(';');
            var entry = (separator < 0 ? rest : rest.Slice(0, separator)).Trim();
            if (entry.Length > 0 && !TryAddGencodes(entry, gencodes))
            {
                invalidEntries.Add(entry.ToString());
            }

            rest = separator < 0 ? ReadOnlySpan<char>.Empty : rest.Slice(separator + 1);
        }

        return new NvccArchitecturePlan(gencodes, invalidEntries);
    }

    /// <summary>
    /// Logs error <see cref="InvalidEntryErrorCode"/> for each invalid entry of <paramref name="plan"/>; returns
    /// <see langword="false"/> when there was one.
    /// </summary>
    public static bool ReportInvalidEntries(TaskLoggingHelper log, NvccArchitecturePlan plan)
    {
        foreach (var entry in plan.InvalidEntries)
        {
            log.LogError(subcategory: null, errorCode: InvalidEntryErrorCode, helpKeyword: null, file: null,
                lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
                message: FormatInvalidEntry(entry));
        }

        return plan.InvalidEntries.Count == 0;
    }

    /// <summary>The error message for an entry that <see cref="Parse"/> found invalid.</summary>
    public static string FormatInvalidEntry(string entry) =>
        $"GSharpTargetArchitectures entry '{entry}' is not a GPU architecture nvcc accepts. " +
        "Write 'compute_XY,sm_XY' (SASS for X.Y), 'compute_XY,compute_XY' (PTX for X.Y), " +
        "'sm_XY' (SASS and PTX for X.Y, as nvcc -arch=sm_XY) or 'compute_XY' (PTX for X.Y); " +
        "an architecture may end in 'a' or 'f'.";

    private static bool TryAddGencodes(ReadOnlySpan<char> entry, List<NvccGencode> gencodes)
    {
        var commaIndex = entry.IndexOf(',');
        if (commaIndex >= 0)
        {
            var arch = entry.Slice(0, commaIndex).Trim();
            var code = entry.Slice(commaIndex + 1).Trim();
            if (!IsArchitecture(arch, VirtualPrefix) ||
                !(IsArchitecture(code, RealPrefix) || IsArchitecture(code, VirtualPrefix)))
            {
                return false;
            }

            gencodes.Add(new NvccGencode(arch.ToString(), code.ToString()));
            return true;
        }

        if (IsArchitecture(entry, VirtualPrefix))
        {
            var text = entry.ToString();
            gencodes.Add(new NvccGencode(text, text));
            return true;
        }

        if (!IsArchitecture(entry, RealPrefix))
        {
            return false;
        }

        // nvcc -arch=sm_XY means -gencode arch=compute_XY,code=[sm_XY,compute_XY]; the suffix stays on both.
        var virtualArch = VirtualPrefix + entry.Slice(RealPrefix.Length).ToString();
        gencodes.Add(new NvccGencode(virtualArch, entry.ToString()));
        gencodes.Add(new NvccGencode(virtualArch, virtualArch));
        return true;
    }

    // <prefix><two or more digits>[a|f]
    private static bool IsArchitecture(ReadOnlySpan<char> text, string prefix)
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
