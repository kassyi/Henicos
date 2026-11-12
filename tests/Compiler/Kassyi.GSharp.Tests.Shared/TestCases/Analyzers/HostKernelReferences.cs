#define HOST_KERNEL_REFERENCE_DEFINED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// GSH0031: every host form that runs or references a [GpuKernel] method; nameof, the calls that C# removes and device
// code are not reported
public static partial class ReferencedKernels
{
    [GpuKernel]
    public static void K(int[] o)
    {
        o[0] = 42;
    }

    [GpuKernel]
    [Conditional("HOST_KERNEL_REFERENCE_NEVER_DEFINED")]
    public static void RemovedK(int[] o)
    {
        o[0] = 42;
    }

    [GpuKernel]
    [Conditional("HOST_KERNEL_REFERENCE_DEFINED")]
    public static void KeptK(int[] o)
    {
        o[0] = 42;
    }

    [GpuKernel]
    public static void CallsAndReferencesK(int[] o)
    {
        K(o);
        Action<int[]> f = K;
    }
}

public struct KernelRange
{
    public int Count;
}

public struct KernelEnumerator
{
    public int Current => 0;

    public bool MoveNext() => false;
}

public struct KernelPair
{
    public int A;
}

public sealed class KernelBag : IEnumerable
{
    public IEnumerator GetEnumerator() => throw new NotSupportedException();
}

public readonly struct KernelWaitable
{
}

public readonly struct KernelAwaiter : INotifyCompletion
{
    public bool IsCompleted => true;

    public void GetResult()
    {
    }

    public void OnCompleted(Action continuation)
    {
    }
}

public static partial class PatternKernels
{
    [GpuKernel]
    public static KernelEnumerator GetEnumerator(this KernelRange range) => default;

    [GpuKernel]
    public static void Deconstruct(this KernelPair pair, out int a, out int b)
    {
        a = pair.A;
        b = 0;
    }

    [GpuKernel]
    public static void Add(this KernelBag bag, int value)
    {
    }

    [GpuKernel]
    public static KernelAwaiter GetAwaiter(this KernelWaitable waitable) => default;
}

// Members that C# calls by their syntax; the declaration check rejects a kernel there, the reference is reported too
public sealed class KernelNumber
{
    public int Value;

    public static int Property
    {
        [GpuKernel]
        get => 0;
        [GpuKernel]
        set
        {
        }
    }

    [GpuKernel]
    public static KernelNumber operator +(KernelNumber a, KernelNumber b) => a;

    [GpuKernel]
    public static explicit operator int(KernelNumber n) => n.Value;
}

public static unsafe class KernelReferencingHost
{
    private static readonly Action<int[]> s_field = ReferencedKernels.K;

    public static event Action<int[]>? Changed;

    public static void Forms(int[] o, int[][] arrays)
    {
        ReferencedKernels.K(o);
        Action<int[]> f = ReferencedKernels.K;
        var created = new Action<int[]>(ReferencedKernels.K);
        var cast = (Action<int[]>)ReferencedKernels.K;
        var natural = ReferencedKernels.K;
        Array.ForEach(arrays, ReferencedKernels.K);
        Changed += ReferencedKernels.K;
        delegate*<int[], void> pointer = &ReferencedKernels.K;
        Action call = () => ReferencedKernels.K(o);
    }

    public static async Task PatternForms(KernelRange range, KernelPair pair, KernelWaitable waitable)
    {
        foreach (var item in range)
        {
        }

        var (a, b) = pair;
        if (pair is (42, _))
        {
        }

        var bag = new KernelBag { 40, 2 };
        await waitable;
    }

    public static void SyntaxForms(KernelNumber n)
    {
        var sum = n + n;
        var value = (int)n;
        var read = KernelNumber.Property;
        KernelNumber.Property = 1;
    }

    public static void NotReported(int[] o)
    {
        _ = nameof(ReferencedKernels.K);
        ReferencedKernels.RemovedK(o);
        Log(ReferencedKernels.K);
        ReferencedKernels.KeptK(o);
    }

    [Conditional("HOST_KERNEL_REFERENCE_NEVER_DEFINED")]
    private static void Log(Action<int[]> kernel)
    {
    }
}
