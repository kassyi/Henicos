using Kassyi.GSharp.Stdlib.Math;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class MEM_01_ControlFlowBypass
{
    public void Test(bool error)
    {
        var ctx = new GpuContext(1024);
        if (error) return;
        ctx.Dispose();
    }
}
