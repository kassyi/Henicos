namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Represents a deferred scalar boolean expression on the GPU.
/// </summary>
public interface IGpuBoolExpr : IDisposable
{
    /// <summary>
    /// Evaluates the boolean expression on the GPU and copies the scalar boolean result to host.
    /// </summary>
    /// <param name="workspace">The GPU workspace memory context.</param>
    /// <returns><c>true</c> if the condition evaluated to true; otherwise, <c>false</c>.</returns>
    bool Evaluate(GpuWorkspace workspace);
}
