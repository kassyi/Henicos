using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Enumerates the iterators of InArgumentConversionCrossFileIterator_Iterator.cs, which are inlined from another syntax tree
[SkipSnapshotConsistency("Needs its iterator from another file")]
public static partial class InArgumentConversionCrossFileIteratorKernels
{
    [GpuKernel]
    public static void ConvertedKernel(int[] o)
    {
        foreach (int v in InArgumentConversionCrossFileIterator.Converted(o.Length))
        {
            o[ThreadIndex.X] = v;
        }
    }

    [GpuKernel]
    public static void ConvertedBeforeWriteKernel(int[] o)
    {
        foreach (int v in InArgumentConversionCrossFileIterator.ConvertedBeforeWrite(o.Length))
        {
            o[ThreadIndex.X] = v;
        }
    }
}
