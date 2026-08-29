using Kassyi.GSharp.Stdlib.Math;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class MEM_01_DiscardedAllocation
{
    public void Test()
    {
        new GpuContext(1024);
    }
}
