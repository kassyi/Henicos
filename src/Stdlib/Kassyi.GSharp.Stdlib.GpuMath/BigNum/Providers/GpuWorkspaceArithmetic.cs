namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// The big-float arithmetic and the execution context of a <see cref="GpuWorkspace"/>, chosen in one place.
/// </summary>
/// <remarks>
/// The operators (<c>GpuBigFloatExpressionVisitor</c>), the vector expressions and <c>GpuBigFloatArray.SetElement</c> must
/// compute and round with the same provider, so they take it from here instead of each writing the choice out.
/// </remarks>
internal static class GpuWorkspaceArithmetic
{
    /// <summary>
    /// Gets the big-float arithmetic of a workspace: the G# kernels (<see cref="GSharpKernelFloatProvider"/>) for the
    /// <see cref="GpuExecutionEngine.GSharpAot"/> engine, the C# reference (<see cref="CSharpCpuBigFloatArithmetic"/>)
    /// otherwise.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <returns>The arithmetic provider.</returns>
    internal static IGpuBigFloatArithmetic FloatArithmetic(GpuWorkspace workspace) =>
        workspace.Engine == GpuExecutionEngine.GSharpAot
            ? GSharpKernelFloatProvider.Instance
            : CSharpCpuBigFloatArithmetic.Instance;

    /// <summary>
    /// Creates an execution context on the memory manager and the stream of a workspace.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <returns>The execution context.</returns>
    internal static GpuBigIntExecutionContext CreateContext(GpuWorkspace workspace) =>
        new(workspace, workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), workspace.Stream);
}
