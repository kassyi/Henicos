using Kassyi.GSharp.Stdlib.Math;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class MEM_01_NullAssignmentWithoutDisposal
{
    public void Test()
    {
        var ctx = new GpuContext(1024);
        ctx = null;
    }
}
