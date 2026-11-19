using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A struct that a kernel uses while only the host calls the members that reach a barrier: truth operators without an
/// operator macro, an operator without a device definition, a method without <c>[GpuDeviceFunction]</c> and a
/// <c>Dispose</c> that no device <c>using</c> calls. None of them is written for the device, so the kernel builds and
/// runs on every path (GSH0062 is reported only for struct member bodies that the device runs).
/// </summary>
public static partial class StructHostOnlyMemberKernels
{
    [GpuDeviceFunction]
    public static int TaggedRotate(int tag)
    {
        SharedMemory<int> shared = new(256);
        int tid = ThreadIndex.X;
        shared[tid] = tag + tid;
        Gpu.SyncThreads();
        return shared[tid ^ 1];
    }

    [GpuKernel]
    public static void HostOnlyMembersKernel(int[] output)
    {
        var h = new HostOnlyMembers { V = ThreadIndex.X };
        output[ThreadIndex.X] = h.V + 1;
    }

    /// <summary>Calls every host-only member, and the constructor of a struct that no kernel uses.</summary>
    public static int UseOnHost()
    {
        var h = new HostOnlyMembers { V = 1 };
        using (h)
        {
        }

        return (h ? 1 : 0) + h.HostCompute() + (h + h).V + new KernelUnusedStruct(1).V;
    }
}

/// <summary>A struct that a kernel uses; only the host calls the members that reach a barrier.</summary>
public struct HostOnlyMembers : IDisposable
{
    public int V;

    public static bool operator true(HostOnlyMembers h) => StructHostOnlyMemberKernels.TaggedRotate(h.V) > 0;

    public static bool operator false(HostOnlyMembers h) => StructHostOnlyMemberKernels.TaggedRotate(h.V) <= 0;

    public static HostOnlyMembers operator +(HostOnlyMembers a, HostOnlyMembers b) =>
        new() { V = StructHostOnlyMemberKernels.TaggedRotate(a.V + b.V) };

    public int HostCompute() => StructHostOnlyMemberKernels.TaggedRotate(V);

    public void Dispose()
    {
        V = StructHostOnlyMemberKernels.TaggedRotate(1);
    }
}

/// <summary>A struct that no device code uses, whose constructor reaches a barrier.</summary>
public struct KernelUnusedStruct
{
    public int V;

    public KernelUnusedStruct(int t)
    {
        V = StructHostOnlyMemberKernels.TaggedRotate(t);
    }
}
