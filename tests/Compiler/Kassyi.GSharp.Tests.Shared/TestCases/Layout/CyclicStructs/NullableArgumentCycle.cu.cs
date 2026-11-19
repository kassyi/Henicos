using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// A<T> holds a Nullable of A<A<T>> (CS0523): the cycle runs through Nullable<T>
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class NullableArgumentCycle
{
    public struct A<T>
    {
        public A<A<T>>? Inner;
        public T X;
    }

    [GpuKernel]
    public static void NullableArgumentKernel(A<int> value, int[] output)
    {
        output[0] = value.X;
    }
}
