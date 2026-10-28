using System;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Local initialization checked by EmissionShapeTests")]
public static partial class SkipLocalsInitKernels
{
    [GpuKernel]
    public static void ZeroInitializedKernel(int[] output, int n)
    {
        int value = default;
        value += n;
        output[0] = value;
        Span<int> zeroedBuf = stackalloc int[4];
        zeroedBuf[0] = n;
        output[1] = zeroedBuf[0];
    }

    [GpuKernel]
    [SkipLocalsInit]
    public static void MethodSkipsKernel(int[] output, int n)
    {
        int value = default;
        value += n;
        output[0] = value;
        Span<int> methodBuf = stackalloc int[4];
        int[] methodArray = new int[4];
        ReadOnlySpan<int> methodReadOnly = stackalloc int[4];
        Span<int> methodSpanArray = new int[4];
        methodBuf[0] = n;
        output[1] = methodBuf[0] + methodArray[n & 3] + methodReadOnly[n & 3] + methodSpanArray[n & 3];
    }

    // [SkipLocalsInit] on a lambda applies to its own body, as Roslyn clears localsinit of the lambda's method
    [GpuKernel]
    public static void LambdaSkipsKernel(int[] output, int n)
    {
        Gpu.GridStrideLoop(n, [SkipLocalsInit] (int i) =>
        {
            Span<int> lambdaSkipsBuf = stackalloc int[4];
            lambdaSkipsBuf[0] = i;
            output[i] = lambdaSkipsBuf[0];
        });
        Gpu.GridStrideLoop(n, (int i) =>
        {
            Span<int> lambdaZeroedBuf = stackalloc int[4];
            lambdaZeroedBuf[0] = i;
            output[i] += lambdaZeroedBuf[0];
        });
    }

    [GpuKernel]
    public static void GenericKernel(int[] output, float[] floats, int n)
    {
        var ints = new SkipLocalsInitGeneric<int> { Seed = n };
        var reals = new SkipLocalsInitGeneric<float> { Seed = n };
        output[0] = ints.Zeroed + ints.Skipped;
        floats[0] = reals.Zeroed + reals.Skipped;
    }

    // The local function inherits [SkipLocalsInit] from the method that contains it
    [GpuKernel]
    [SkipLocalsInit]
    public static void LocalFunctionInSkippingMethodKernel(int[] output, int n)
    {
        int Read(int i)
        {
            Span<int> outerSkipsBuf = stackalloc int[4];
            outerSkipsBuf[0] = i;
            return outerSkipsBuf[0];
        }

        output[0] = Read(n);
    }

    // [SkipLocalsInit] on a local function applies to its own body only
    [GpuKernel]
    public static void LocalFunctionSkipsKernel(int[] output, int n)
    {
        Span<int> outerZeroedBuf = stackalloc int[4];
        outerZeroedBuf[0] = n;

        [SkipLocalsInit]
        int Read(int i)
        {
            Span<int> ownSkipsBuf = stackalloc int[4];
            ownSkipsBuf[0] = i;
            return ownSkipsBuf[0];
        }

        output[0] = Read(outerZeroedBuf[0]);
    }

    [GpuKernel]
    public static void PropertySkipsKernel(int[] output, int n)
    {
        var holder = new SkipLocalsInitProperty { Seed = n };
        output[0] = holder.Skipped + holder.Zeroed;
    }
}

public struct SkipLocalsInitProperty
{
    public int Seed;

    public int Zeroed
    {
        get
        {
            Span<int> propertyZeroedBuf = stackalloc int[4];
            propertyZeroedBuf[0] = Seed;
            return propertyZeroedBuf[0];
        }
    }

    [SkipLocalsInit]
    public int Skipped
    {
        get
        {
            Span<int> propertySkipsBuf = stackalloc int[4];
            propertySkipsBuf[0] = Seed;
            return propertySkipsBuf[0];
        }
    }
}

// G# has no generic device functions; a stackalloc of a type parameter appears in a member of a generic struct
public struct SkipLocalsInitGeneric<T> where T : unmanaged
{
    public T Seed;

    public T Zeroed
    {
        get
        {
            Span<T> genericZeroedBuf = stackalloc T[4];
            genericZeroedBuf[0] = Seed;
            return genericZeroedBuf[0];
        }
    }

    [SkipLocalsInit]
    public T Skipped
    {
        get
        {
            Span<T> genericSkipsBuf = stackalloc T[4];
            genericSkipsBuf[0] = Seed;
            return genericSkipsBuf[0];
        }
    }
}

[SkipLocalsInit]
[SkipSnapshotConsistency("Local initialization checked by EmissionShapeTests")]
public static partial class SkipLocalsInitTypeKernels
{
    [GpuKernel]
    public static void TypeSkipsKernel(int[] output, int n)
    {
        int value = default;
        value += n;
        output[0] = value;
        Span<int> typeBuf = stackalloc int[4];
        typeBuf[0] = n;
        output[1] = typeBuf[0];
    }

    // [SkipLocalsInit] on a type applies to the types nested in it
    public static partial class Nested
    {
        [GpuKernel]
        public static void NestedTypeSkipsKernel(int[] output, int n)
        {
            Span<int> nestedBuf = stackalloc int[4];
            nestedBuf[0] = n;
            output[0] = nestedBuf[0];
        }
    }
}
