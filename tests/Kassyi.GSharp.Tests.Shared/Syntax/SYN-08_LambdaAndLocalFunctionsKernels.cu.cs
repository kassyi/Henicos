// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class SYN_08_LambdaAndLocalFunctionsKernels
{
    [GpuKernel]
    public static void NonCapturingLambdaKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        // Simple expression lambda without capture
        var doubleValue = (int x) => x * 2;
        data[tid] = doubleValue(data[tid]);
    }

    [GpuKernel]
    public static void ValueCapturingLambdaKernel(int[] data, int factor)
    {
        int tid = ThreadIndex.X;
        // Capturing variable 'factor' and array pointer 'data'
        var multiply = (int x) => x * factor;
        data[tid] = multiply(data[tid]);
    }

    [GpuKernel]
    public static void ImmediatelyEvaluatedLambdaKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        // Local function
        int LocalAdd(int a, int b)
        {
            return a + b;
        }
        data[tid] = LocalAdd(data[tid], 100);
    }

    [GpuKernel]
    public static void ShadowedVariableCaptureKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        int value = 50;
        // Anonymous method with shadowed local variable (parameter name 'value' shadows local 'value')
        var calc = delegate(int value) {
            return value * 2;
        };
        data[tid] = calc(data[tid]) + value; // calc(100) + 50
    }

    [GpuKernel]
    public static float NestedReturnsKernel(int someValue)
    {
        // Local function with return
        float LocalAdd(float a, float b)
        {
            return a + b;
        }

        var doubleValue = (float x) => {
            return x * 2;
        };

        return LocalAdd(someValue, doubleValue(someValue));
    }
}
