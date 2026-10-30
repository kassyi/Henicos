namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The divergence of the barriers of a device function that a caller in another assembly needs (B7), one bit per
/// parameter by ordinal (parameters past the 32nd share the last bit).
/// </summary>
/// <param name="ConditionParameters">
/// The parameters that make a barrier diverge (<see cref="BarrierDivergence.Condition"/>) when they receive a
/// thread-variant argument, where uniform arguments do not.
/// </param>
/// <param name="ExitOnlyParameters">
/// The parameters that make the threads leave early before a barrier (<see cref="BarrierDivergence.ExitOnly"/>) when
/// they receive a thread-variant argument, where uniform arguments do not.
/// </param>
/// <param name="HasExitOnlyBarrier">
/// Whether the threads may leave early before a barrier even with uniform arguments.
/// </param>
internal readonly record struct ExportedBarrierDivergence(
    int ConditionParameters,
    int ExitOnlyParameters,
    bool HasExitOnlyBarrier);
