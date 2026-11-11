using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// The value steps of the command-specific rows of <see cref="FuzzOptionTable"/>: each sets one member of
/// <see cref="FuzzOptionValues"/> and returns the error message, or <see langword="null"/> when the value is valid.
/// </summary>
internal static class FuzzOptionSetters
{
    internal static string? ParseSize(FuzzOptionValues values, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) ||
            size is < 1 or > 8)
        {
            return "--size requires an integer in [1, 8].";
        }

        values.Size = size;
        return null;
    }

    internal static string? ParseProfileCode(FuzzOptionValues values, string value)
    {
        values.ProfileCode = KnownIssueFilter.NormalizeProfile(value);
        return null;
    }

    internal static string? ParseSourcePath(FuzzOptionValues values, string value)
    {
        values.SourcePath = value;
        return null;
    }

    internal static string? ParseKernelName(FuzzOptionValues values, string value)
    {
        values.KernelName = value;
        return null;
    }

    internal static string? ParseTestCmd(FuzzOptionValues values, string value)
    {
        values.TestCmd = value;
        return null;
    }

    internal static string? ParseOutPath(FuzzOptionValues values, string value)
    {
        values.OutPath = value;
        return null;
    }

    internal static string? ParseOutDir(FuzzOptionValues values, string value)
    {
        values.OutDir = value;
        return EmptyDirectoryError("--out-dir", value);
    }

    internal static string? ParseWorkDir(FuzzOptionValues values, string value)
    {
        values.WorkDir = value;
        return EmptyDirectoryError("--work-dir", value);
    }

    internal static string? ParseRepoRoot(FuzzOptionValues values, string value)
    {
        values.RepoRoot = value;
        return EmptyDirectoryError("--repo-root", value);
    }

    internal static string? ParseCudaArch(FuzzOptionValues values, string value)
    {
        values.CudaArch = value;
        return null;
    }

    internal static string? ParseFailingPaths(FuzzOptionValues values, string value)
    {
        values.FailingPaths = value;
        return null;
    }

    internal static string? ParseFailureKind(FuzzOptionValues values, string value)
    {
        values.FailureKind = value;
        return null;
    }

    internal static string? ParseColumn(FuzzOptionValues values, string value)
    {
        values.Column = value;
        return null;
    }

    internal static string? ParseFindingsDir(FuzzOptionValues values, string value)
    {
        values.FindingsDir = value;
        return null;
    }

    // The value is set before it is checked: an error stops the parse and the values are dropped.
    private static string? EmptyDirectoryError(string name, string value) =>
        value.Length == 0 ? $"{name} requires a directory path." : null;
}
