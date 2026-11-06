using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>A file parsed once for each set of preprocessor symbols that was needed to reach all of its code.</summary>
/// <param name="Path">The repository-relative path.</param>
/// <param name="IsGenerated">True when the header marks the file as generated; it has no variants to measure.</param>
/// <param name="Variants">One context per symbol set, starting with the empty set.</param>
/// <param name="UnmeasuredBranchLines">The 1-based lines of <c>#if</c>, <c>#elif</c> and <c>#else</c> directives whose code no symbol set made active.</param>
internal sealed record ParsedFile(string Path, bool IsGenerated, ImmutableArray<DetectorContext> Variants, ImmutableArray<int> UnmeasuredBranchLines);
