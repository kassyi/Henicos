// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class SYN_01_09_ControlFlowKernels
{
    // GSH-01 / SYN-05: Simple ternary with implicit type conversion
    [GpuKernel]
    public static void SimpleTernaryKernel(int[] a, int[] b, double[] result)
    {
        int tid = ThreadIndex.X;
        // Implicit type conversion (int vs double)
        result[tid] = a[tid] > b[tid] ? a[tid] : b[tid] * 1.5;
    }

    // GSH-01 / SYN-05: Deeply nested ternary logic via nested if-else
    [GpuKernel]
    public static void NestedTernaryKernel(int[] a, int[] b, int[] c, int[] result)
    {
        int tid = ThreadIndex.X;
        int x = a[tid];
        int y = b[tid];
        int z = c[tid];
        
        result[tid] = x > y 
            ? (y > z ? 1 : 2)
            : (x > z ? 3 : 4);
    }

    // SYN-01: Switch statement pattern match logic
    [GpuKernel]
    public static void SwitchExpressionKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X;
        int val = input[tid];
        output[tid] = val switch
        {
            1 => 10,
            2 => 20,
            3 => 30,
            _ => 99
        };
    }

    [GpuKernel]
    public static void RefSwitchExpressionKernel(int[] a, int[] b, int[] selectors, int[] valuesToAssign)
    {
        int tid = ThreadIndex.X;
        if (selectors[tid] == 1)
        {
            a[tid] = valuesToAssign[tid];
        }
        else
        {
            b[tid] = valuesToAssign[tid];
        }
    }

    // SYN-04: Null coalescing check on Nullable<T> (T?)
    [GpuKernel]
    public static void NullCoalescingKernel(int?[] nullableInputs, int[] fallbackValues, int[] results)
    {
        int tid = ThreadIndex.X;
        int? input = nullableInputs[tid];
        results[tid] = input ?? fallbackValues[tid];
    }

    // SYN-04 Edge Case: Null coalescing compound assignment (??=) logic with side-effect in LHS index
    [GpuKernel]
    public static void NullCoalescingAssignmentSideEffectKernel(int?[] targetArray, int[] sideEffectTracker, int[] values)
    {
        int tid = ThreadIndex.X;
        int idx = sideEffectTracker[tid];
        sideEffectTracker[tid] = idx + 1;
        
        targetArray[idx] ??= values[tid];
    }

    // SYN-09: Default Literal and default(T)
    [GpuDeviceFunction]
    public static int DefaultLiteralHelper(int val, Vector2 vec)
    {
        return val + (int)vec.X + (int)vec.Y;
    }

    [GpuKernel]
    public static void DefaultLiteralKernel(int[] valOut, float[] vecOut, int[] condOut)
    {
        int tid = ThreadIndex.X;
        int x = default;
        Vector2 v = default;

        valOut[tid] = x + DefaultLiteralHelper(default, default);
        vecOut[tid] = v.X + v.Y;
        condOut[tid] = tid == 0 ? default : 100;
    }

    // GEN-21: Single statement body wrapping
    [GpuKernel]
    public static void SingleStatementWrappingKernel(int[] condA, int[] condB, int[] results)
    {
        int tid = ThreadIndex.X;
        int res = 0;

        // Unbracketed if-else
        if (condA[tid] > 0)
            res += 10;
        else
            res += 20;

        // Dangling else scenario: if (c1) if (c2) a=1; else b=1;
        if (condA[tid] > 5)
            if (condB[tid] > 5)
                res += 100;
            else
                res += 200;

        // Single statement for loop
        for (int i = 0; i < 3; i++)
            res += i;

        // Single statement while loop
        int count = 0;
        while (count < 2)
            count++;

        results[tid] = res + count;
    }
}
