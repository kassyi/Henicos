using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// Cycles (CS0523) of several shapes as array elements: the GSH0080 reason names what holds itself by value
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class CycleReasons
{
    public struct Cyc
    {
        public Cyc Inner;
        public int X;
    }

    public struct N
    {
        public N? Inner;
        public int X;
    }

    public struct S<T>
    {
        public S<T> F;
        public T X;
    }

    public struct NG<T>
    {
        public NG<T>? Inner;
        public T X;
    }

    public struct X<T>
    {
        public Y<T> F;
        public T A;
    }

    public struct Y<T>
    {
        public X<T> G;
        public T B;
    }

    public struct O
    {
        public float A;
        public X<int> I;
    }

    public struct Box<T>
    {
        public T V;
    }

    public struct Loop
    {
        public Box<Loop> B;
        public int X;
    }

    [GpuKernel]
    public static void CycKernel(Cyc[] values, int[] output) => output[0] = values.Length;

    [GpuKernel]
    public static void NullableKernel(N[] values, int[] output) => output[0] = values.Length;

    [GpuKernel]
    public static void GenericKernel(S<int>[] values, int[] output) => output[0] = values.Length;

    [GpuKernel]
    public static void GenericNullableKernel(NG<int>[] values, int[] output) => output[0] = values.Length;

    [GpuKernel]
    public static void RingKernel(X<int>[] first, Y<int>[] second, O[] outer, int[] output) =>
        output[0] = first.Length + second.Length + outer.Length;

    [GpuKernel]
    public static void LoopKernel(Loop[] loops, Box<Loop>[] boxes, int[] output) =>
        output[0] = loops.Length + boxes.Length;
}
