namespace Kassyi.GSharp.Cuda;

/// <summary>
/// The line at the top of a generated CUDA module that names the functions whose register spills are not reported as
/// GS0001: <c>// gsharp:suppress GS0001 &lt;name&gt; &lt;name&gt; ...</c>, the C++ names separated by spaces.
/// </summary>
/// <remarks>
/// The generator writes it (<c>SpillSuppressionPlan</c>), the build task reads it (<c>SpillSuppression</c>); both
/// compile this file, so the format has one definition.
/// </remarks>
internal static class SpillSuppressionLine
{
    /// <summary>The start of the line; the names follow after a space.</summary>
    public const string Marker = "// gsharp:suppress GS0001";
}
