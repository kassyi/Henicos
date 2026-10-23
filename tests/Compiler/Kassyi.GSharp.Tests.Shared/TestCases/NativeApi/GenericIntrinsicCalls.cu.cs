using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.NativeApi;

// Compiled together with the stubs emitted from GenericIntrinsics.yml.
public interface ITestShape
{
}

public struct TestShape : ITestShape
{
}

public struct TestFragment<TShape, T>
    where TShape : struct, ITestShape
    where T : unmanaged
{
    public T Value;
}

public static partial class GenericIntrinsicCallKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Needs the stubs emitted from GenericIntrinsics.yml")]
    public static void CopyAndReleaseKernel(int[] dst, int[] src)
    {
        int tid = ThreadIndex.X;
        TestIntrinsics.CopyAsync(ref dst[tid], in src[tid]);
        TestIntrinsics.CopyAsync<int>(ref dst[tid], ref src[tid]);
        TestIntrinsics.Unsafe.Release(tid);
        dst[tid] += TestIntrinsics.Unsafe.Deep.Count();
    }
}
