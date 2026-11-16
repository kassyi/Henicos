using System.Collections.Immutable;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// The partition plan for ahead-of-time native compilation of an assembly.
/// </summary>
/// <param name="UnitCount">The number of translation units B (clamped between 1 and 32).</param>
/// <param name="KernelUnitIndices">The unit index assigned to each input kernel, in original input order.</param>
/// <param name="UnitByteCounts">The total byte count of kernels assigned to each unit.</param>
internal sealed record NativeModulePlan(
    int UnitCount,
    ImmutableArray<int> KernelUnitIndices,
    ImmutableArray<long> UnitByteCounts);
