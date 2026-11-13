namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>Reads the <c>GSharpTargetArchitectures</c> entries as nvcc reads its architecture options.</summary>
internal static class NvccArchitectures
{
    /// <summary>The MSBuild error code for an entry that is not a GPU architecture nvcc accepts.</summary>
    public const string InvalidEntryErrorCode = "GS0004";

    /// <summary>
    /// Turns each entry into its <c>-gencode</c> options, as <see cref="CudaArchitectureList"/> reads them, and
    /// collects the entries that are not a GPU architecture nvcc accepts.
    /// </summary>
    /// <param name="targetArchitectures">The entries separated by <c>;</c>; empty entries are skipped.</param>
    public static NvccArchitecturePlan Parse(string targetArchitectures)
    {
        var gencodes = new List<NvccGencode>();
        var invalidEntries = new List<string>();
        var rest = (targetArchitectures ?? "").AsSpan();
        while (CudaArchitectureList.TryReadEntry(ref rest, out var entry))
        {
            var count = CudaArchitectureList.ReadGencodes(entry, out var first, out var second);
            if (count == 0)
            {
                invalidEntries.Add(entry.ToString());
                continue;
            }

            gencodes.Add(first);
            if (count == 2)
            {
                gencodes.Add(second);
            }
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
}
