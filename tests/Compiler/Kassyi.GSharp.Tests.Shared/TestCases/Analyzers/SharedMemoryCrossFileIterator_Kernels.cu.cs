using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// Enumerates the iterator of SharedMemoryCrossFileIterator_Iterator.cs, which is inlined from another syntax tree
[SkipSnapshotConsistency("Needs its iterator from another file")]
public static partial class SharedMemoryCrossFileIteratorKernels
{
    [GpuKernel]
    public static void SharedIterKernel(int[] o)
    {
        foreach (int v in SharedMemoryCrossFileIterator.Values())
        {
            o[ThreadIndex.X] = v;
        }
    }
}
