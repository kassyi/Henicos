// ReSharper disable All
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels whose local functions are declared in one state of the CPU state machine and called from another: the
/// barrier splits the body, and a function that stays in the block of its own state is not visible in the others.
/// </summary>
/// <summary>A resource that appends its <see cref="Id"/> to <see cref="Log"/> when it is disposed.</summary>
/// <remarks><c>Log[0]</c> counts the entries; entry <c>n</c> is at <c>Log[n]</c>.</remarks>
public struct LocalFunctionAcrossStatesRecorder : IDisposable
{
    public int[] Log;
    public int Id;

    public void Dispose()
    {
        Log[0] = Log[0] + 1;
        Log[Log[0]] = Id;
    }
}

public static partial class LocalFunctionAcrossStatesKernels
{
    // The function is declared before the barrier and called after it; it captures a local that crosses the barrier
    [GpuKernel]
    public static void DeclaredBeforeBarrierKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int z = 5;
        int Cap() => z;
        Gpu.SyncThreads();
        data[tid] += Cap();
    }

    // The function is called in two states and declared in the last one
    [GpuKernel]
    public static void CalledBeforeDeclarationKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = Seven();
        Gpu.SyncThreads();
        data[tid] += Seven();

        static int Seven() => 7;
    }

    // The first use is in unreachable code of the first state
    [GpuKernel]
    public static void UnreachableFirstUseKernel(int[] data)
    {
        int tid = ThreadIndex.X;
#pragma warning disable CS0162 // The call under test is unreachable
        if (false)
        {
            data[tid] = Cap();
        }
#pragma warning restore CS0162

        int z = 5;
        Gpu.SyncThreads();
        data[tid] += Cap();

        int Cap() => z;
    }

    // The captured local is declared after the last barrier, so no barrier crosses it
    [GpuKernel]
    public static void CapturesLocalAfterLastBarrierKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        int w = 3;
        data[tid] += Get();

        int Get() => w;
    }

    // Two functions of one name in two sibling scopes that the lowering flattens into one list
    [GpuKernel]
    public static void SameNameInTwoLoopsKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        for (int k = 0; k < 2; k++)
        {
            int Sq(int v) => v * v + k;
            data[tid] += Sq(1);
            Gpu.SyncThreads();
            data[tid] += Sq(1);
        }

        for (int k = 0; k < 2; k++)
        {
            int Sq(int v) => v + 10 * k;
            Gpu.SyncThreads();
            data[tid] += Sq(2);
        }
    }

    // The function declaration has a label, which a goto after the barrier jumps to
    [GpuKernel]
    public static void LabeledLocalFunctionKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int n = 0;
    Again:
        int One() => 1;
        n += One();
        Gpu.SyncThreads();
        if (n < 3)
        {
            goto Again;
        }

        data[tid] += n;
    }

    // A recursive function declared before the barrier
    [GpuKernel]
    public static void RecursionAcrossBarrierKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int Fact(int v) => v <= 1 ? 1 : v * Fact(v - 1);
        Gpu.SyncThreads();
        data[tid] += Fact(3);
    }

    // The body of Gpu.SyncGrid is split at the grid sync; the function is declared at its start
    [GpuKernel(Cooperative = true)]
    public static void SyncGridBodyKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            void Add()
            {
                data[tid] += 1;
            }

            data[tid] = 10;
            Add();
        });
    }

    // The declaration is in a nested block of a device function that returns before it
    [GpuDeviceFunction]
    public static int NestedAfterReturn(int[] s, int tid)
    {
        s[tid] += 1;
        Gpu.SyncThreads();
        if (tid >= 0)
        {
            int r = s[tid] + Two();
            return r;

            static int Two() => 2;
        }

        return 0;
    }

    [GpuKernel]
    public static void NestedAfterReturnKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = NestedAfterReturn(data, tid);
    }

    // The declaration is at the top level of a device function, after its return
    [GpuDeviceFunction]
    public static int TopLevelAfterReturn(int[] s, int tid)
    {
        s[tid] += 1;
        Gpu.SyncThreads();
        return s[tid] + Two();

        static int Two() => 2;
    }

    [GpuKernel]
    public static void TopLevelAfterReturnKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = TopLevelAfterReturn(data, tid);
    }

    // A using declaration after the last barrier, captured by a function: the function stays in its state, and the
    // using variable keeps its disposal
    [GpuKernel]
    public static void UsingCapturedKernel(int[] log, int[] o)
    {
        Gpu.SyncThreads();
        using var r = new LocalFunctionAcrossStatesRecorder { Log = log, Id = 7 };
        o[0] = Read();
        int Read() => r.Id;
    }

    [GpuDeviceFunction]
    public static int SplitParity(int x, out int rest)
    {
        rest = x % 2;
        return x / 2;
    }

    // An out variable after the last barrier, captured by a function: it is not a declarator, so the function stays
    [GpuKernel]
    public static void OutVariableCapturedKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        int q = SplitParity(data[tid], out var r);
        data[tid] = F() + q * 0;
        int F() => r + 10;
    }

    // A function that calls a function that stays (the second captures a using variable) stays as well
    [GpuKernel]
    public static void ChainToStayingFunctionKernel(int[] log, int[] o)
    {
        Gpu.SyncThreads();
        using var w = new LocalFunctionAcrossStatesRecorder { Log = log, Id = 5 };
        o[0] = A();
        int A() => B() + 1;
        int B() => w.Id;
    }
}
