using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct StackScopeGuard : IDisposable
{
    public uint[] Arr;
    public int I;

    public void Dispose()
    {
        Arr[I] += 0x100000u;
    }
}

public struct StackScopeTagGuard : IDisposable
{
    public uint[] Arr;
    public int I;
    public uint Tag;

    public void Dispose()
    {
        Arr[I] = (Arr[I] << 4) | Tag;
    }
}

/// <summary>
/// Kernels and device functions with <c>using</c> declarations, <c>using</c> statements, and <c>fixed</c> statements
/// whose bodies reach <see cref="Warp.ActiveMask"/> or <see cref="Gpu.SyncThreads"/>.
/// </summary>
[SkipSnapshotConsistency("Verified by StackScopeTests on the stack models and CUDA")]
public static unsafe partial class StackScopeKernels
{
    [GpuKernel]
    public static void UsingDeclarationMaskKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        using var gd = new StackScopeGuard { Arr = result, I = g };
        result[g] += Warp.ActiveMask() & 0xFu;
    }

    [GpuKernel]
    public static void ContinueBeforeUsingDeclarationKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        for (int i = 0; i < 3; i++)
        {
            if ((t + i) % 2 == 0)
            {
                continue;
            }

            using var gd = new StackScopeGuard { Arr = result, I = g };
            result[g] += Warp.ActiveMask() & 0xFu;
        }
    }

    [GpuKernel]
    public static void ContinueInsideUsingStatementKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        for (int i = 0; i < 3; i++)
        {
            if ((t + i) % 3 == 0)
            {
                continue;
            }

            using (var gd = new StackScopeGuard { Arr = result, I = g })
            {
                if ((t + i) % 3 == 1)
                {
                    continue;
                }

                result[g] += Warp.ActiveMask() & 0xFFu;
            }
        }
    }

    [GpuKernel]
    public static void BranchInsideUsingStatementKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        using (var gd = new StackScopeGuard { Arr = result, I = g })
        {
            if (t % 2 == 0)
            {
                result[g] += Warp.ActiveMask() & 0xFFu;
            }

            result[g] += (Warp.ActiveMask() & 0xFFu) << 8;
        }
    }

    [GpuKernel]
    public static void UsingDeclarationInBranchKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        if (t % 2 == 0)
        {
            using var gd = new StackScopeGuard { Arr = result, I = g };
            result[g] += Warp.ActiveMask() & 0xFFu;
        }

        result[g] += (Warp.ActiveMask() & 0xFFu) << 8;
    }

    [GpuDeviceFunction]
    public static int SelectIndexWithBranchMask(uint[] result, int g, int t)
    {
        if (t % 2 == 0)
        {
            result[g] += Warp.ActiveMask() & 0xFu;
        }

        return g;
    }

    [GpuKernel]
    public static void UsingDeclarationHeaderCallKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        using var gd = new StackScopeGuard { Arr = result, I = SelectIndexWithBranchMask(result, g, t) };
        result[g] += (Warp.ActiveMask() & 0xFFu) << 8;
    }

    [GpuDeviceFunction]
    public static uint ReadGuardedMask(uint[] result, int g, int t)
    {
        if (t == 1)
        {
            return 7u;
        }

        using var gd = new StackScopeGuard { Arr = result, I = g };
        if (t == 2)
        {
            return 8u;
        }

        return Warp.ActiveMask() & 0xFFu;
    }

    [GpuKernel]
    public static void UsingDeclarationInDeviceFunctionKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        uint v = ReadGuardedMask(result, g, t);
        result[g] += v;
        result[g] += (Warp.ActiveMask() & 0xFFu) << 8;
    }

    [GpuKernel]
    public static void ContinueAfterUsingDeclarationKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        for (int i = 0; i < 3; i++)
        {
            using var gd = new StackScopeGuard { Arr = result, I = g };
            if ((t + i) % 2 == 0)
            {
                continue;
            }

            result[g] += Warp.ActiveMask() & 0xFu;
        }
    }

    [GpuKernel]
    public static void TwoUsingDeclarationsWithBreakAndContinueKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        for (int i = 0; i < 3; i++)
        {
            using var g1 = new StackScopeGuard { Arr = result, I = g };
            if (t == 3 && i == 1)
            {
                break;
            }

            using var g2 = new StackScopeGuard { Arr = result, I = g };
            if ((t + i) % 2 == 0)
            {
                continue;
            }

            result[g] += Warp.ActiveMask() & 0xFu;
        }

        result[g] += (Warp.ActiveMask() & 0xFFu) << 8;
    }

    [GpuKernel]
    public static void ContinueInsideFixedInLoopKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        for (int i = 0; i < 3; i++)
        {
            if ((t + i) % 3 == 0)
            {
                continue;
            }

            fixed (int* p = data)
            {
                if ((t + i) % 3 == 1)
                {
                    continue;
                }

                uint? m = t > 2 ? Warp.ActiveMask() : null;
                result[g] = result[g] * 3u + (uint)p[g] + (m ?? 9u);
            }
        }
    }

    [GpuKernel]
    public static void FixedMaskKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        fixed (int* p = data)
        {
            result[g] = (uint)p[g] * 0x1000u + (Warp.ActiveMask() & 0xFFu);
        }
    }

    [GpuKernel]
    public static void BranchInsideFixedKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        fixed (int* p = data)
        {
            if (t % 2 == 0)
            {
                result[g] += Warp.ActiveMask() & 0xFFu;
            }

            result[g] += (Warp.ActiveMask() & 0xFFu) << 8;
        }
    }

    [GpuKernel]
    public static void BarrierLoopContinueInsideFixedKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        for (int i = 0; i < 3; i++)
        {
            Gpu.SyncThreads();
            fixed (int* p = data)
            {
                if ((t + i) % 3 == 1)
                {
                    continue;
                }

                result[g] = result[g] * 3u + (uint)p[g];
            }
        }
    }

    [GpuKernel]
    public static void UsingDeclarationDisposeOrderSingleKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        using var a = new StackScopeTagGuard { Arr = result, I = g, Tag = 0xDu };
        result[g] += Warp.ActiveMask() & 0xFu;
    }

    [GpuKernel]
    public static void UsingDeclarationDisposeOrderNestedKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        using var a = new StackScopeTagGuard { Arr = result, I = g, Tag = 1u };
        using var b = new StackScopeTagGuard { Arr = result, I = g, Tag = 2u };
        result[g] += Warp.ActiveMask() & 0xFu;
    }

    [GpuKernel]
    public static void LocalFunctionAfterUsingDeclarationKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        int a = AddOne(1);
        using var guard = new StackScopeGuard { Arr = result, I = g };
        int AddOne(int v) => v + 1;
        result[g] += (uint)a + (Warp.ActiveMask() & 0xFu);
    }

    [GpuKernel]
    public static void LocalFunctionCapturingPriorConstAfterUsingDeclarationKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        const int C = 5;
        int a = AddConst(1);
        using var guard = new StackScopeGuard { Arr = result, I = g };
        int AddConst(int v) => v + C;
        result[g] += (uint)a + (Warp.ActiveMask() & 0xFu);
    }

    [GpuKernel]
    public static void GotoLoopCrossingUsingStatementKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        int n = 0;
    Loop:
        result[g] += (Warp.ActiveMask() & 0xFFu) << (n * 8);
        using (var gd = new StackScopeGuard { Arr = result, I = g })
        {
            if ((t & 3) != 0 && n++ < 1)
            {
                goto Loop;
            }

            result[g] += (Warp.ActiveMask() & 0xFFu) << 24;
        }
    }

    [GpuKernel]
    public static void GotoLoopCrossingFixedStatementKernel(int[] data, uint[] result)
    {
        int t = ThreadIndex.X;
        int g = BlockIndex.X * BlockDimension.X + t;
        int n = 0;
    Loop:
        result[g] += (Warp.ActiveMask() & 0xFFu) << (n * 8);
        fixed (int* p = data)
        {
            if ((t & 3) != 0 && n++ < 1)
            {
                goto Loop;
            }

            result[g] += ((uint)p[g] << 16) + ((Warp.ActiveMask() & 0xFFu) << 24);
        }
    }
}
