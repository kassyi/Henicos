namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What a call recorded by <see cref="MemoryUniformity"/> may write, read once per call.</summary>
/// <param name="Summary">The callee's own analysis, when it is a device function of this compilation.</param>
/// <param name="WritesMemory">The call may write any memory (<see cref="DeviceFunctionEffect.Memory"/>).</param>
/// <param name="WritesReceiver">
/// The call may write the struct it is called on through <c>this</c> (a mutating struct member).
/// </param>
internal readonly record struct MemoryCallEffect(CalleeVariance Summary, bool WritesMemory, bool WritesReceiver);
