namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What a call of a device function gives its caller (<see cref="UniformityValueReader"/>).</summary>
/// <param name="Return">
/// The variance of the value it returns, a value returned under a divergent branch included.
/// </param>
/// <param name="ByReference">The variance of what it leaves in its <c>ref</c> and <c>out</c> parameters.</param>
/// <param name="Barrier">Whether only some threads reach a barrier in it (§3.2.4, the composed summary).</param>
/// <param name="MemoryAtExit">
/// For each parameter by ordinal, how far the memory it reaches may differ where the function returns, after its own
/// barriers (C9); default when the function was not analyzed (a recursive call).
/// </param>
/// <param name="FieldMemoryAtExit">The same for the memory that the fields of its struct parameters reach.</param>
/// <param name="BarrierOnEveryPath">
/// The barrier that every path to its return passes: <see cref="ThreadVariance.Uniform"/> a grid barrier,
/// <see cref="ThreadVariance.Grid"/> a block barrier, <see cref="ThreadVariance.Block"/> none.
/// </param>
internal readonly record struct CalleeVariance(
    ThreadVariance Return,
    ThreadVariance ByReference,
    BarrierDivergence Barrier,
    ImmutableArray<ThreadVariance> MemoryAtExit,
    ThreadVariance FieldMemoryAtExit,
    ThreadVariance BarrierOnEveryPath);
