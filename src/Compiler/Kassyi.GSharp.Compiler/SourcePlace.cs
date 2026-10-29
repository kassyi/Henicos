namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The place of a construct in the user's C# source as the CPU paths name it in a run-time error, for example
/// <c>Kernels.cu.cs(12,17)</c>: the file name without its directory (generated code embeds no absolute path) and the
/// 1-based line and column. Used by the warp shuffle reads and the shared memory placement of the CPU paths.
/// </summary>
internal static class SourcePlace
{
    /// <summary>
    /// The place written when a node has no location in the user's source: a test without a semantic model, or a tree
    /// without a file path.
    /// </summary>
    public const string Unknown = "an unknown place";

    /// <summary>
    /// Formats the place of <paramref name="node"/>, which may be in a lowered tree (its original-span annotation maps it
    /// back, <see cref="SemanticModelExtensions.GetReportLocation"/>).
    /// </summary>
    public static string Format(SemanticModel? semanticModel, SyntaxNode node)
    {
        if (semanticModel?.GetReportLocation(node) is not { } location || location == Location.None)
        {
            return Unknown;
        }

        var span = location.GetLineSpan();
        var fileName = Path.GetFileName(span.Path);
        if (string.IsNullOrEmpty(fileName))
        {
            return Unknown;
        }

        var start = span.StartLinePosition;
        return $"{fileName}({start.Line + 1},{start.Character + 1})";
    }
}
