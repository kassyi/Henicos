using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.IteratorTypeResolver;

public ref struct RefOnly
{
    public int Value;
}

public struct WithManagedField
{
    public string Name;
}

public struct Plain
{
    public int Value;
}

public static unsafe class ElementTypes
{
    // Each parameter is one candidate element type of an iterator
    public static void Candidates<TFree, TUnmanaged>(
        TFree typeParameter,
        TUnmanaged unmanagedTypeParameter,
        SharedMemory<int> sharedMemory,
        RefOnly refStruct,
        int* pointer,
        delegate*<void> functionPointer,
        int[] array,
        string managedClass,
        WithManagedField managedStruct,
        int primitive,
        Plain plainStruct)
        where TUnmanaged : unmanaged
    {
    }
}

public static class Iterators
{
    [GpuDeviceFunction]
    public static IEnumerable<int> Valid()
    {
        yield return 1;
    }

    public static IEnumerable<int> WithoutAttribute()
    {
        yield return 1;
    }

    [GpuDeviceFunction]
    public static IEnumerable<T> Generic<T>()
        where T : unmanaged
    {
        yield return default;
    }

    [GpuDeviceFunction]
    public static IEnumerable<string> ManagedElement()
    {
        yield return "";
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> ReturnsAnother()
    {
        return Valid();
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> YieldOnlyInLocalFunction()
    {
        return Local();

        static IEnumerable<int> Local()
        {
            yield return 1;
        }
    }

    [GpuDeviceFunction]
    public static int TryAndLockWithoutYield(int[] data)
    {
        try
        {
            lock (data)
            {
                data[0] = 1;
            }
        }
        finally
        {
            data[1] = 2;
        }

        return data[0];
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Self(int n)
    {
        yield return n;
        foreach (var x in Self(n - 1))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> PingA(int n)
    {
        foreach (var x in PingB(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> PingB(int n)
    {
        foreach (var x in PingA(n))
        {
            yield return x;
        }
    }

    // Calls a helper that calls another helper, none of which comes back
    [GpuDeviceFunction]
    public static IEnumerable<int> CallsHelpers(int n)
    {
        yield return Helper(n);
        foreach (var x in Valid())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static int Helper(int n) => Helper2(n) + Helper2(n);

    [GpuDeviceFunction]
    public static int Helper2(int n) => n;
}

public static class Instance
{
    public sealed class Holder
    {
        [GpuDeviceFunction]
        public IEnumerable<int> InstanceIterator()
        {
            yield return 1;
        }
    }
}

public static class Counts
{
    // 32 yield returns, all inside an if
    [GpuDeviceFunction]
    public static IEnumerable<int> ThirtyTwoInIf(bool c)
    {
        if (c)
        {
            yield return 0; yield return 1; yield return 2; yield return 3;
            yield return 4; yield return 5; yield return 6; yield return 7;
            yield return 8; yield return 9; yield return 10; yield return 11;
            yield return 12; yield return 13; yield return 14; yield return 15;
            yield return 16; yield return 17; yield return 18; yield return 19;
            yield return 20; yield return 21; yield return 22; yield return 23;
            yield return 24; yield return 25; yield return 26; yield return 27;
            yield return 28; yield return 29; yield return 30; yield return 31;
        }

        yield break;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Four()
    {
        yield return 0;
        yield return 1;
        yield return 2;
        yield return 3;
    }

    // Each of the 4 elements of Four() runs 9 yield returns: 36 copies
    [GpuDeviceFunction]
    public static IEnumerable<int> NineForEachOfFour()
    {
        foreach (var a in Four())
        {
            yield return a; yield return a; yield return a;
            yield return a; yield return a; yield return a;
            yield return a; yield return a; yield return a;
        }
    }

    // A foreach over an array is not an expansion: its body counts once
    [GpuDeviceFunction]
    public static IEnumerable<int> TwoInsideArrayLoop(int[] data)
    {
        foreach (var a in data)
        {
            yield return a;
            yield return a;
        }
    }
}

public static class ForEachSources
{
    public static void Consumers(bool c, IEnumerable<int> other, IEnumerable<int>[] slots, int[] array)
    {
        IEnumerable<int> x = null;

        var whenTrue = c ? Iterators.Valid() : null;
        foreach (var v in whenTrue) { }

        var whenFalse = c ? null : Iterators.Valid();
        foreach (var v in whenFalse) { }

        // The left side is a local of type IEnumerable<T> (a parameter is not an iterator value)
        IEnumerable<int> otherLocal = other;
        var coalesceLeft = otherLocal ?? null;
        foreach (var v in coalesceLeft) { }

        var coalesceRight = (IEnumerable<int>)null ?? Iterators.Valid();
        foreach (var v in coalesceRight) { }

        var assignedLocal = (x = null);
        foreach (var v in assignedLocal) { }

        var assignedValue = (slots[0] = Iterators.Valid());
        foreach (var v in assignedValue) { }

        var plainArray = array;
        foreach (var v in plainArray) { }
    }
}
