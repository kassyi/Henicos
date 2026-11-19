using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// An <c>Action</c> without type arguments as a local: the CPU dialect writes it as <c>System_Action&lt;&gt;</c>, the
/// alias of the Prelude with an empty argument list, and the CUDA dialect as <c>auto</c>.
/// </summary>
public static partial class ActionLocalKernels
{
    [GpuKernel]
    public static void ActionLocalKernel(int[] dst)
    {
        int t = ThreadIndex.X;
        Action f = () => dst[t] = t + 1;
        f();
    }

    [GpuKernel]
    public static void ActionMethodGroupKernel(int[] dst)
    {
        int t = ThreadIndex.X;
        Action f = Set;
        f();

        void Set() => dst[t] = t + 3;
    }

    // The local is held across the barrier, so the CPU state machine keeps it in its frame
    [GpuKernel]
    public static void ActionAcrossBarrierKernel(int[] dst)
    {
        int t = ThreadIndex.X;
        Action f = () => dst[t] += 1;
        Gpu.SyncThreads();
        f();
    }
}
