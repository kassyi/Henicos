#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>A struct argument whose <c>[GpuReadOnly]</c> array field is passed to a suspending call.</summary>
public struct ReadOnlySuspensionContext
{
    [GpuReadOnly] public int[] Source;
    public int[] Output;
}

/// <summary>
/// Kernels that keep a <c>[GpuReadOnly]</c> array across a barrier: as the argument temporary of a suspending call, in a
/// hoisted local, and through a local that holds another local. The context field keeps the <c>const</c> elements.
/// </summary>
public static partial class ReadOnlyArraySuspensionKernels
{
    [GpuDeviceFunction]
    public static void CopyAfterBarrier([GpuReadOnly] int[] source, int[] output)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        output[tid] = source[tid];
    }

    // A read-only struct field passed to a suspending call (spilled into an argument temporary)
    [GpuKernel]
    public static void FieldArgumentKernel(ReadOnlySuspensionContext ctx) => CopyAfterBarrier(ctx.Source, ctx.Output);

    // A local that holds a read-only parameter across a barrier (hoisted into the context)
    [GpuKernel]
    public static void HoistedLocalKernel([GpuReadOnly] int[] source, int[] output)
    {
        var view = source;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        output[tid] = view[tid];
    }

    // A local initialized from a local that holds a read-only field
    [GpuKernel]
    public static void ChainedLocalKernel(ReadOnlySuspensionContext ctx, int[] output)
    {
        int[] first = ctx.Source;
        int[] second = first;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        output[tid] = second[tid] + first[tid];
    }

    // A writable array kept across the barrier is still a writable field
    [GpuKernel]
    public static void WritableLocalKernel(int[] data)
    {
        int[] view = data;
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        view[tid] = view[tid] * 2;
    }
}
