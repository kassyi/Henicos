namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// Reads the CUDA release from the output of <c>nvcc --version</c> and checks it against the one G# needs.
/// </summary>
internal static class NvccVersion
{
    /// <summary>The MSBuild error code for an nvcc older than <see cref="MinimumMajor"/>.</summary>
    public const string TooOldErrorCode = "GS0006";

    /// <summary>The oldest CUDA Toolkit major release that G# builds with.</summary>
    public const int MinimumMajor = NvccMinimum.MajorRelease;

    private const string ReleaseMarker = "release ";

    /// <summary>
    /// Reads <c>major.minor</c> from the line <c>Cuda compilation tools, release 13.4, V13.4.59</c> of
    /// <paramref name="versionOutput"/>; <see langword="false"/> when the output has no such line.
    /// </summary>
    public static bool TryParseRelease(string versionOutput, out int major, out int minor)
    {
        minor = 0;
        var start = versionOutput.IndexOf(ReleaseMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            major = 0;
            return false;
        }

        var rest = versionOutput.AsSpan(start + ReleaseMarker.Length);
        if (!TryReadNumber(ref rest, out major) || rest.IsEmpty || rest[0] != '.')
        {
            return false;
        }

        rest = rest.Slice(1);
        return TryReadNumber(ref rest, out minor);
    }

    /// <summary>
    /// Logs error <see cref="TooOldErrorCode"/> and returns <see langword="false"/> when
    /// <paramref name="versionOutput"/> names a release older than <see cref="MinimumMajor"/>. An output without a
    /// release line passes, so a wrapper that prints its own text is not rejected.
    /// </summary>
    public static bool ReportTooOld(TaskLoggingHelper log, string nvccPath, string versionOutput)
    {
        if (!TryParseRelease(versionOutput, out var major, out var minor) || major >= MinimumMajor)
        {
            return true;
        }

        log.LogError(subcategory: null, errorCode: TooOldErrorCode, helpKeyword: null, file: null,
            lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,
            message: FormatTooOld(nvccPath, major, minor));
        return false;
    }

    /// <summary>The error message for an nvcc of CUDA <paramref name="major"/>.<paramref name="minor"/>.</summary>
    public static string FormatTooOld(string nvccPath, int major, int minor) =>
        $"G# needs the CUDA Toolkit {MinimumMajor}.0 or later; the nvcc run as '{nvccPath}' is CUDA {major}.{minor}. " +
        "Put a newer nvcc first on PATH, or set GSHARP_TEST_NO_CUDA=1 to build without CUDA.";

    // Reads the decimal digits at the start of text (at most 9, so the value fits an int) and moves past them.
    private static bool TryReadNumber(ref ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        var length = 0;
        while (length < text.Length && length < 9 && text[length] >= '0' && text[length] <= '9')
        {
            value = (value * 10) + (text[length] - '0');
            length++;
        }

        text = text.Slice(length);
        return length > 0;
    }
}
