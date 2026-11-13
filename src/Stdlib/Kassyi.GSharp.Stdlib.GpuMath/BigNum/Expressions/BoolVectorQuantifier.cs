namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Any / All over the first Count flags of a vector: the kernels read the count on the device, so the
/// flags after it do not take part, and an empty vector gives false / true, as System.Linq.
/// </summary>
internal static class BoolVectorQuantifier
{
    public static bool Evaluate(GpuWorkspace workspace, IGpuBoolVectorExpression source, bool all)
    {
        DeviceCounted.ThrowIfCpuFallback(workspace);
        if (source.Capacity == 0)
        {
            return EvaluateEmpty(workspace, source, all);
        }

        var flags = DeviceCounted.Evaluate(source, workspace);
        using var result = new GpuBigUIntArray(workspace, 1, 1);
        workspace.Provider.WriteWord(result.DevicePointer, 0, all ? 1u : 0u, workspace.Stream);

        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorBoolReduction
        {
            Flags = flags.Flags.ToProxy(),
            Result = result.ToProxy(),
            Count = flags.Count.ToContext()
        };

        var gridDimX = (uint)((flags.Count.Capacity + 255) / 256);
        if (all)
        {
            GpuBigIntVectorKernels.LaunchVectorAllTrueKernelPtr(ctx, gridDimX, 1, 1, 256, 1, 1, 0, workspace.Stream);
        }
        else
        {
            GpuBigIntVectorKernels.LaunchVectorAnyTrueKernelPtr(ctx, gridDimX, 1, 1, 256, 1, 1, 0, workspace.Stream);
        }

        return workspace.Provider.ReadWord(result.DevicePointer, 0, workspace.Stream) != 0;
    }

    // A vector with no capacity has no element: nothing is launched, and nothing is evaluated unless the evaluation
    // can throw (inputs of different lengths)
    private static bool EvaluateEmpty(GpuWorkspace workspace, IGpuBoolVectorExpression source, bool all)
    {
        DeviceCounted.EvaluateIfMayThrow(source, workspace);
        return all;
    }
}
