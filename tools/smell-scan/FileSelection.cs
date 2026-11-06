using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The files a run looks at, sorted and without duplicates, and the requested files that do not exist.</summary>
/// <param name="Files">Repository-relative paths with <c>/</c> separators.</param>
/// <param name="Missing">Requested paths that are not files on disk.</param>
internal sealed record FileSelection(ImmutableArray<string> Files, ImmutableArray<string> Missing);
