using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// A<T, U> holds a Nullable of A<U, Wrap<T>> (CS0523): the shadows of two Nullable constructions share one name
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class NullableSwapCycle
{
    public struct Wrap<T>
    {
        public T V;
    }

    public struct A<T, U>
    {
        public A<U, Wrap<T>>? F;
        public int X;
    }

    [GpuKernel]
    public static void SwapKernel(A<int, long> value, A<int, long>[] values, int[] output)
    {
        output[0] = values.Length;
    }
}
