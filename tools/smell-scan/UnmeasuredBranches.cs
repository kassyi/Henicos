using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Conditional directives of a file whose code was never active in any symbol set that was tried.</summary>
/// <param name="Path">The file.</param>
/// <param name="Lines">The 1-based lines of the directives, ascending.</param>
internal sealed record UnmeasuredBranches(string Path, ImmutableArray<int> Lines);
