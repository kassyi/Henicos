namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorCountExpression([TakesOwnership] IGpuBigIntVectorExpression vector)
    : IGpuBigIntExpression, IDisposable
{
    public int NumWords => 1;

    // A length known on the host is written with a host copy, which also works under the CPU fallback and in a CUDA
    // graph for a chain that cannot throw; the vector is evaluated first only when it can, since System.Linq runs
    // every projection it counts. A device count (after Where) evaluates the vector and lets a kernel write the count,
    // so that no host synchronization is needed and a CUDA graph counts again on every launch
    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        if (vector.TryGetLength(out var length))
        {
            DeviceCounted.EvaluateIfMayThrow(vector, workspace);
            return WriteHostCount(workspace, targetResult, length);
        }

        var count = DeviceCounted.Evaluate(vector, workspace).Count;
        var destination = targetResult ?? new GpuBigUInt(workspace, NumWords);
        try
        {
            var ctx = new GpuBigIntVectorKernels.AotContexts.ElementCountWrite
            {
                Destination = destination.ToProxy(),
                DestinationWords = destination.NumWords,
                Count = count.ToContext()
            };
            GpuBigIntVectorKernels.LaunchWriteElementCountKernelPtr(ctx, 1, 1, 1, 32, 1, 1, 0, workspace.Stream);
            return new GpuBigInt(destination, false);
        }
        catch when (!targetResult.HasValue)
        {
            destination.Dispose();
            throw;
        }
    }

    private static GpuBigInt WriteHostCount(GpuWorkspace workspace, GpuBigUInt? targetResult, int length)
    {
        if (targetResult is { } target)
        {
            var words = new uint[target.NumWords];
            words[0] = (uint)length;
            workspace.Provider.CopyHostToDevice(target.DevicePointer, words, words.Length, workspace.Stream);
            return new GpuBigInt(target, false);
        }

        return new GpuBigInt(new GpuBigUInt(workspace, new[] { (uint)length }), false);
    }

    public void Dispose() => vector.Dispose();
}
