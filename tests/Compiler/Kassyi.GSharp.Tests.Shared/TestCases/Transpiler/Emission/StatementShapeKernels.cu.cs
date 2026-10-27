using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

public struct EarlyReturnConstructor
{
    public int A;

    public EarlyReturnConstructor(int a)
    {
        A = a;
        if (a > 0)
        {
            return;
        }

        A = -a;
    }
}

[SkipSnapshotConsistency("Emission shapes checked line by line by EmissionShapeTests")]
public static partial class StatementShapeKernels
{
    [GpuDeviceFunction]
    public static bool TryGet(int[] data, out int value)
    {
        value = data[0];
        return value > 0;
    }

    [GpuDeviceFunction]
    public static int ReturnWithOutVar(int[] data)
    {
        return TryGet(data, out var v) ? v : 0;
    }

    [GpuDeviceFunction]
    public static int SuspendingReturnWithOutVar(int[] data)
    {
        Gpu.SyncThreads();
        return TryGet(data, out var w) ? w : 0;
    }

    [GpuKernel]
    public static void LoopFormsKernel(int[] data, int[] output, int n)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        foreach (int typed in data)
        {
            sum += typed;
        }

        int i;
        int j;
        for (i = 0, j = n; i < j; i++, j--)
        {
            sum += i * j;
        }

        foreach (var picked in TryGet(data, out var first) ? data : output)
        {
            sum += picked + first;
        }

        sum += ReturnWithOutVar(data);
        sum += SuspendingReturnWithOutVar(data);
        sum += new EarlyReturnConstructor(tid).A;

        // The out var inside the lambda's block is declared there, not before the declaration of the lambda
        var addFound = (int x) =>
        {
            int r = TryGet(data, out var found) ? found + x : x;
            return r;
        };
        sum += addFound(tid);
        output[tid] = sum;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> TwoValues(int start)
    {
        yield return start;
        yield return start + 1;
    }

    // The loop body is copied once per yield return; its label and goto must be renamed together in each copy
    [GpuKernel]
    public static void LabelInIteratorBodyKernel(int[] output, int skip, int stop)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        foreach (var x in TwoValues(tid))
        {
            if (x == stop)
            {
                break;
            }

            if (x == skip)
            {
                goto Skip;
            }

            sum += x;
        Skip:;
        }

        output[tid] = sum;
    }

    // The same loop in each iteration of Gpu.ForUnroll: the user's label gets the iteration's suffix, the labels of the
    // expansion do not
    [GpuKernel]
    public static void LabelInUnrolledIteratorBodyKernel(int[] output, int skip, int stop)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        Gpu.ForUnroll(0, 2, i =>
        {
            foreach (var x in TwoValues(tid + i))
            {
                if (x == stop)
                {
                    break;
                }

                if (x == skip)
                {
                    goto Skip;
                }

                sum += x;
            Skip:;
            }
        });

        output[tid] = sum;
    }
}
