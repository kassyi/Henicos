#define CONDITIONAL_FILE_ON
using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace ConditionalCallCase;

public struct CallValue
{
    public int V;

    [GpuOperatorMacro(typeof(CallOps), nameof(CallOps.Add))]
    public static CallValue operator +(CallValue left, CallValue right) => throw new GpuOnlyException();
}

public static partial class CallOps
{
    [GpuDeviceFunction]
    public static void Add(ref CallValue result, in CallValue left, in CallValue right)
    {
        result.V = left.V + right.V;
    }

    // Defined only by the #define of this file
    [GpuDeviceFunction]
    [Conditional("CONDITIONAL_FILE_ON")]
    public static void Kept(int[] output)
    {
        output[0] += 1;
    }

    [GpuDeviceFunction]
    [Conditional("CONDITIONAL_FILE_ON")]
    public static void Marker(int[] output)
    {
        output[0] += 10;
    }

    [GpuDeviceFunction]
    [Conditional("CONDITIONAL_NEVER_DEFINED")]
    public static void Removed(int[] output)
    {
        output[0] += 100;
    }

    // Expands the operator macro, so the managed output copies its body into a managed version
    [GpuDeviceFunction]
    public static int Sum(int[] output, CallValue a, CallValue b)
    {
        Kept(output);
        Removed(output);
        for (int k = 0; k < 1; k++, Removed(output))
        {
        }

        CallValue sum = default;
        sum = a + b;
        return sum.V;
    }
}

public static partial class DefineKernels
{
    [GpuKernel]
    public static void DefineKernel(int[] output)
    {
        CallOps.Kept(output);
        CallOps.Removed(output);
        output[1] = CallOps.Sum(output, default, default);
    }

    // The iterator is written in a file without the #define: C# removes its call of Marker
    [GpuKernel]
    public static void IteratorKernel(int[] output)
    {
        foreach (var value in CallIterators.Values(output))
        {
            output[2] += value;
        }
    }
}
