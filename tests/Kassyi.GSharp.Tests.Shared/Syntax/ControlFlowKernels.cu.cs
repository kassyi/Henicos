// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static partial class ControlFlowKernels
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
}
