#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

public struct StackBodyLocalsPair
{
    public uint A;
}

/// <summary>
/// Kernels that reach Warp.ActiveMask() whose locals the lowering for the stacks leaves out of the scope of their uses,
/// untyped (out var of a call that splits) or not definitely assigned (assigned in the right operand of &amp;&amp;,
/// || or the condition of ?:). StackBodyLocalsTests runs them on every path; the generator test builds them with the
/// managed stack model (ManagedStackModelGeneratorTests).
/// </summary>
[SkipSnapshotConsistency("Verified by StackBodyLocalsTests and ManagedStackModelGeneratorTests")]
public static partial class StackBodyLocalsKernels
{
    [GpuDeviceFunction]
    private static bool TryMask(int tid, out uint m)
    {
        m = 0u;
        if (tid % 2 == 0)
        {
            m = Warp.ActiveMask();
            return true;
        }

        return false;
    }

    [GpuDeviceFunction]
    private static void GetMask(int tid, out uint m)
    {
        if (tid % 2 == 0)
        {
            m = Warp.ActiveMask();
            return;
        }

        m = 0u;
    }

    [GpuDeviceFunction]
    private static void GetPair(int tid, out StackBodyLocalsPair p)
    {
        p = default;
        if (tid % 2 == 0)
        {
            p.A = Warp.ActiveMask();
        }
    }

    [GpuDeviceFunction]
    private static void Swap(int x, out int h) => h = Warp.ShuffleXorSync(0xFFFFFFFFu, x, 1);

    [GpuDeviceFunction]
    private static void Wait(int x, out int h)
    {
        Gpu.SyncThreads();
        h = x;
    }

    [GpuDeviceFunction]
    private static uint MaskOr(int tid) => tid > 1 && TryMask(tid, out uint m) ? m : 7u;

    // A local assigned in the right operand of &&
    [GpuKernel]
    public static void AssignedInAndKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        uint v;
        if (tid > 3 && (v = Warp.ActiveMask()) != 0u)
        {
            result[tid] = v;
        }

        result[tid] |= Warp.ActiveMask();
    }

    // A local assigned in the right operand of ||
    [GpuKernel]
    public static void AssignedInOrKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        uint v;
        if (tid < 3 || (v = Warp.ActiveMask()) == 0u)
        {
            result[tid] = 7u;
        }
        else
        {
            result[tid] = v;
        }
    }

    // out uint m of a call that splits, in the right operand of &&
    [GpuKernel]
    public static void OutVarInAndKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        if (tid > 1 && TryMask(tid, out uint m))
        {
            result[tid] = m;
        }

        result[tid] |= Warp.ActiveMask() << 8;
    }

    // out var m of a call that splits, as a statement
    [GpuKernel]
    public static void OutVarStatementKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        GetMask(tid, out var m);
        result[tid] = m;
    }

    // out var m of a call that splits, in an if condition
    [GpuKernel]
    public static void OutVarConditionKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        if (TryMask(tid, out var m))
        {
            result[tid] = m;
        }

        result[tid] |= Warp.ActiveMask() << 8;
    }

    // A local declared before and passed out in the right operand of &&
    [GpuKernel]
    public static void PredeclaredOutInAndKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        uint m;
        if (tid > 1 && TryMask(tid, out m))
        {
            result[tid] = m;
        }

        result[tid] |= Warp.ActiveMask() << 8;
    }

    // A local assigned in the condition of ?:
    [GpuKernel]
    public static void AssignedInTernaryConditionKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        uint v;
        uint r = (tid > 3 && (v = Warp.ActiveMask()) != 0u) ? v : 0u;
        result[tid] = r;
    }

    // out uint m in the condition of a ?: that a device function returns
    [GpuKernel]
    public static void ReturnTernaryOutVarKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = MaskOr(tid);
    }

    // out uint m in the right operand of && under !, used after an early return
    [GpuKernel]
    public static void NegatedConditionEarlyReturnKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        if (!(tid > 1 && TryMask(tid, out uint m)))
        {
            return;
        }

        result[tid] = m;
    }

    // A local assigned by a shuffle in the right operand of &&, in a kernel that reaches Warp.ActiveMask()
    [GpuKernel]
    public static void ShuffleAssignedInAndKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        int s;
        if (tid > 3 && (s = Warp.ShuffleXorSync(0xFFFFFFF0u, tid, 1)) > 0)
        {
            result[tid] = (uint)s;
        }

        result[tid] |= Warp.ActiveMask() << 8;
    }

    // A local of the method assigned in the right operand of && inside a loop
    [GpuKernel]
    public static void AssignedInLoopKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        int i = 0;
        uint v = 0u;
        uint last;
        while (i < 3)
        {
            i++;
            if (i > tid % 3 && (last = Warp.ActiveMask()) != 0u)
            {
                v += last & 1u;
            }
        }

        result[tid] = v;
    }

    // out var and out uint of calls that split, in switch sections
    [GpuKernel]
    public static void OutVarInSwitchSectionKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        switch (tid % 3)
        {
            case 0:
                GetMask(tid, out var a);
                result[tid] = a;
                break;
            case 1:
                GetMask(tid, out uint b);
                result[tid] = b;
                break;
            default:
                break;
        }
    }

    // out var of a struct type
    [GpuKernel]
    public static void OutVarStructKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        GetPair(tid, out var p);
        result[tid] = p.A;
    }

    // out uint m in the right operand of && inside a switch section
    [GpuKernel]
    public static void OutVarAndInSwitchSectionKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        switch (tid % 2)
        {
            case 0:
                if (tid > 1 && TryMask(tid, out uint m))
                {
                    result[tid] = m;
                }

                break;
            default:
                break;
        }
    }

    // out var of a callee that shuffles, in a kernel that reaches Warp.ActiveMask()
    [GpuKernel]
    public static void OutVarOfShuffleCalleeKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        Swap(tid, out var h);
        result[tid] = (uint)h + Warp.ActiveMask();
    }

    // out var of a callee that waits at a block barrier, in a kernel that reaches Warp.ActiveMask()
    [GpuKernel]
    public static void OutVarOfBarrierCalleeKernel(uint[] result)
    {
        int tid = ThreadIndex.X;
        Wait(tid, out var h);
        result[tid] = (uint)h + Warp.ActiveMask();
    }
}
