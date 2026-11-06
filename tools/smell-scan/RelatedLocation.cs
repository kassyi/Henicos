namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Another place that belongs to a finding, such as the other parts of a partial type.</summary>
/// <param name="Path">The file, relative to the repository root with <c>/</c> separators.</param>
/// <param name="Range">The place in the file.</param>
/// <param name="Message">What the place is.</param>
internal sealed record RelatedLocation(string Path, SourceRange Range, string Message);
