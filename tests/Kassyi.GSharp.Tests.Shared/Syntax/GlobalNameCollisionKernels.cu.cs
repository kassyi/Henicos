// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax.GlobalNames;

// Each pair below used to be emitted under one C++ name (A_B_C, Outer_Inner, G), so both types of a pair are used by
// the same kernel and carry different values.
public struct Container
{
    public struct Nested
    {
        public struct Leaf
        {
            public int First;
            public int Second;
        }
    }

    public struct Nested_Leaf
    {
        public int Total;
    }
}

public static class Alpha
{
    public struct Outer
    {
        public struct Inner
        {
            public int Value;
        }
    }
}

public static class Beta
{
    public struct Outer_Inner
    {
        public int Value;
    }
}

public struct Pair<T>
{
    public T Value;
}

public struct Pair<T, U>
{
    public T First;
    public U Second;
}

// Device functions with the same name in two types used to form one C++ overload set, so B.Scale(intArg) called
// A.Scale(int), which C# would never select.
public static class FirstScaler
{
    [GpuDeviceFunction]
    public static int Scale(int value) => value * 10;
}

public static class SecondScaler
{
    [GpuDeviceFunction]
    public static int Scale(long value) => (int)value * 100;

    // C# picks Pick(long) for an int argument; C++ finds int -> long and int -> double equally good (ambiguous).
    [GpuDeviceFunction]
    public static int Pick(double value) => 1;

    [GpuDeviceFunction]
    public static int Pick(long value) => 2;

    [GpuDeviceFunction]
    public static int Twice(int value) => Scale(value) * 2;
}

public static partial class GlobalNameCollisionKernels
{
    // A.Foo(int) and B.Foo(long)
    [GpuKernel]
    public static void SameNameDeviceFunctionsKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int value = tid + 1;
        output[tid] = FirstScaler.Scale(value) + SecondScaler.Scale(value) + SecondScaler.Twice(value) * 10000;
    }

    // Overloads within one type are resolved by C#, not by C++.
    [GpuKernel]
    public static void OverloadResolutionKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = SecondScaler.Pick(tid) * 10 + SecondScaler.Pick(1.5);
    }

    // A.B.C and A.B_C
    [GpuKernel]
    public static void UnderscoreInNameKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        Container.Nested.Leaf leaf = new Container.Nested.Leaf { First = 1, Second = 2 };
        Container.Nested_Leaf joined = new Container.Nested_Leaf { Total = 40 };
        output[tid] = leaf.First * 1000 + leaf.Second * 100 + joined.Total + tid;
    }

    // N1.Outer.Inner and N2.Outer_Inner
    [GpuKernel]
    public static void NestedTypeAndUnderscoreKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        Alpha.Outer.Inner nested = new Alpha.Outer.Inner { Value = 7 };
        Beta.Outer_Inner flat = new Beta.Outer_Inner { Value = 30 };
        output[tid] = nested.Value * 100 + flat.Value + tid;
    }

    // G<T> and G<T, U>
    [GpuKernel]
    public static void GenericArityKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        Pair<int> single = new Pair<int> { Value = 5 };
        Pair<int, int> pair = new Pair<int, int> { First = 6, Second = 7 };
        output[tid] = single.Value * 100 + pair.First * 10 + pair.Second + tid;
    }
}
