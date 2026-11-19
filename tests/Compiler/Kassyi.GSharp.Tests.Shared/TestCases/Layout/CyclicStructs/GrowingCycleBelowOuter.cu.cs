using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Layout.CyclicStructs;

// G<T> holds G<G<T>> (CS0523); Outer<T> holds G<T>, and Outer<Outer<int>> holds Outer<int>: the by-value search of
// Outer meets G<T>, G<G<T>>, ... and must stop where G grows
[SkipSnapshotConsistency("Analyzer test case: CyclicStructLayoutTests")]
public static partial class GrowingCycleBelowOuter
{
    public struct G<T>
    {
        public G<G<T>> Inner;
        public T X;
    }

    public struct Outer<T>
    {
        public G<T> Held;
        public T V;
    }

    [GpuKernel]
    public static void OuterKernel(Outer<Outer<int>> value, int[] output)
    {
        output[0] = value.V.V;
    }
}
