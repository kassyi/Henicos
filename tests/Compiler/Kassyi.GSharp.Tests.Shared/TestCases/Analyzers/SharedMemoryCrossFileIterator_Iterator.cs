using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// An iterator that declares shared memory, called from another file (SharedMemoryCrossFileIterator_Kernels.cu.cs)
public static partial class SharedMemoryCrossFileIterator
{
    [GpuDeviceFunction]
    public static IEnumerable<int> Values()
    {
        var s = new SharedMemory<int>(32);
        s[ThreadIndex.X] = ThreadIndex.X;
        int v = s[ThreadIndex.X];
        yield return v;
    }
}
