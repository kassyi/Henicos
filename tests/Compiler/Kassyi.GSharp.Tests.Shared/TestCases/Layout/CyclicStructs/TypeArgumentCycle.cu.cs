using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// A<T> holds B<A<A<T>>>, whose field is A<A<T>> (CS0523): the cycle runs through the type argument of another struct
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class TypeArgumentCycle
{
    public struct B<U>
    {
        public U V;
    }

    public struct A<T>
    {
        public B<A<A<T>>> Inner;
        public T X;
    }

    [GpuKernel]
    public static void TypeArgumentKernel(A<int> value, int[] output)
    {
        output[0] = value.X;
    }
}
