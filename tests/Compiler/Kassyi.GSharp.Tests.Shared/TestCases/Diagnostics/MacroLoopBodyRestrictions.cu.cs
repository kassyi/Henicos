using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct MacroLoopBodyState
{
    public int Total;
}

// GSH0003: a return statement, or an assignment to the loop variable, in the body of Gpu.ForUnroll or
// Gpu.GridStrideLoop. The first kernels report; the last two record what stays legal.
public static partial class MacroLoopBodyRestrictionKernels
{
    [GpuDeviceFunction]
    public static void Bump(ref int value)
    {
        value++;
    }

    [GpuDeviceFunction]
    public static void Reset(out int value)
    {
        value = 0;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ForUnrollReturnKernel(int[] data)
    {
        Gpu.ForUnroll(0, 4, i =>
        {
            if (i == 2) return;
            data[i] += 1;
        });
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void GridStrideReturnKernel(int[] data)
    {
        Gpu.GridStrideLoop(data.Length, i =>
        {
            if (i >= 8) return;
            data[i] += 1;
        });
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ForUnrollContextReturnKernel(int[] data)
    {
        var state = new MacroLoopBodyState();
        Gpu.ForUnroll(ref state, 0, 4, (ref MacroLoopBodyState s, int i) =>
        {
            if (i == 2) return;
            s.Total += i;
        });
        data[0] = state.Total;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void GridStrideContextReturnKernel(int[] data)
    {
        var state = new MacroLoopBodyState();
        Gpu.GridStrideLoop(ref state, data.Length, (ref MacroLoopBodyState s, int i) =>
        {
            if (i >= 8) return;
            s.Total += i;
        });
        data[0] = state.Total;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ForUnrollAssignmentKernel(int[] data)
    {
        Gpu.ForUnroll(0, 4, i =>
        {
            i = 0;
            i += 1;
            i++;
            --i;
            Bump(ref i);
            Reset(out i);
            (i, data[0]) = (1, 2);
            data[i] += 1;
        });
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void GridStrideAssignmentKernel(int[] data)
    {
        Gpu.GridStrideLoop(ref data[0], data.Length, (ref int total, int i) =>
        {
            i += 1;
            data[i] += 1;
        });
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static unsafe void ForUnrollReferenceKernel(int[] data)
    {
        Gpu.ForUnroll(0, 4, i =>
        {
            data[0] += ReadIn(in i);
            int* address = &i;
        });
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void GridStrideReferenceKernel(int[] data)
    {
        Gpu.GridStrideLoop(data.Length, i =>
        {
            data[0] += ReadIn(in i);
        });
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void AllowedBodyKernel(int[] data)
    {
        var state = new MacroLoopBodyState();
        Gpu.ForUnroll(ref state, 0, 4, (ref MacroLoopBodyState s, int i) =>
        {
            // The context is a ref parameter that the body is meant to change; a local of the body is its own
            s.Total += i;
            int local = i;
            local++;
            data[i] += local;
        });
        Gpu.GridStrideLoop(ref state, data.Length, (ref MacroLoopBodyState s, int i) =>
        {
            s = default;
            int index = i;
            index += 1;
            data[i] += index;
        });
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NestedFunctionKernel(int[] data)
    {
        Gpu.ForUnroll(0, 4, i =>
        {
            // A return belongs to the function that contains it, and a parameter of the same name is another variable
            int Twice(int x)
            {
                return x * 2;
            }

            System.Func<int, int> shadow = i => { i = 1; return i; };
            data[i] += Twice(i) + shadow(i);
        });
    }

    [GpuDeviceFunction]
    public static int ReadIn(in int value)
    {
        return value;
    }
}
