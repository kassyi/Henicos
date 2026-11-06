namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>One number a detector measured at one place. It carries no threshold; the judge compares it.</summary>
/// <param name="Detector">The detector that measured it.</param>
/// <param name="Metric">The unit of <paramref name="Value"/>, such as <c>codeLines</c>.</param>
/// <param name="Path">The file, relative to the repository root with <c>/</c> separators.</param>
/// <param name="Range">The place to report; it can differ between <c>#if</c> variants of the same unit.</param>
/// <param name="Symbol">The qualified name of the measured member.</param>
/// <param name="Value">The measured number.</param>
/// <param name="PhysicalLines">The number of lines the node spans, for line-based metrics; otherwise 0.</param>
/// <param name="UnitPosition">The position that identifies the measured unit in the file (see <see cref="UnitAnchor"/>).</param>
/// <param name="Group">
/// Names a unit that is spread over several files, such as a partial type; measurements of the same group in the same
/// project are added up before they are judged. <see langword="null"/> for a unit that sits in one place.
/// </param>
internal sealed record Measurement(
    string Detector,
    string Metric,
    string Path,
    SourceRange Range,
    string Symbol,
    int Value,
    int PhysicalLines,
    int UnitPosition,
    string? Group = null);
