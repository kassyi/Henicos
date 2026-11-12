namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>The kernel arguments and the launch of the floating-point vector comparisons.</summary>
internal static class FloatVectorCompareLaunch
{
    public static GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly Operand(GpuBigFloatArrayView vector) =>
        new()
        {
            Limbs = vector.Significands.ToProxy(),
            Exponents = vector.Exponents.ToProxy(),
            Signs = vector.Signs.ToProxy(),
            NumWords = vector.WordsPerElement
        };

    // One block per element, as wide as the wider side (32 to 1024 threads)
    public static void Launch(
        GpuWorkspace workspace,
        GpuBigIntVectorKernels.AotContexts.VectorFloatCompare ctx,
        int capacity,
        int leftWords,
        int rightWords)
    {
        var blockDimX = (uint)Math.Clamp(Math.Max(leftWords, rightWords), 32, 1024);
        GpuBigIntVectorKernels.LaunchVectorFloatCompareKernelPtr(
            ctx, (uint)capacity, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);
    }
}
