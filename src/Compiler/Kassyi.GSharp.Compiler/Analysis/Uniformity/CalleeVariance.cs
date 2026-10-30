namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What a call of a device function gives its caller (<see cref="UniformityValueReader"/>).</summary>
/// <param name="Return">
/// The variance of the value it returns, a value returned under a divergent branch included.
/// </param>
/// <param name="ByReference">The variance of what it leaves in its <c>ref</c> and <c>out</c> parameters.</param>
/// <param name="Barrier">Whether only some threads reach a barrier in it (§3.2.4, the composed summary).</param>
internal readonly record struct CalleeVariance(
    ThreadVariance Return,
    ThreadVariance ByReference,
    BarrierDivergence Barrier);
