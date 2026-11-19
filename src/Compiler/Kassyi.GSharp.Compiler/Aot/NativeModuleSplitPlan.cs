using System.Collections.Immutable;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// Pure partition plan assigning each kernel index to a unit file before any <see cref="SourceWriter"/> runs.
/// </summary>
internal sealed record NativeModuleSplitPlan(
    string? HeaderFileName,
    ImmutableArray<string> UnitFileNames,
    ImmutableArray<ImmutableArray<int>> UnitKernelIndices)
{
    public bool IsSplit => HeaderFileName is not null;
}
