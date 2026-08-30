using Kassyi.GSharp.Stdlib.Math;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class MEM_01_FundamentalOwnershipValid
{
    public void TestScopedDisposal()
    {
        using var ctx = new GpuContext(1024);
    }

    public void TestExplicitManualDisposal()
    {
        var ctx = new GpuContext(1024);
        ctx.Dispose();
    }

    public GpuContext TestReturnValueTransfer()
    {
        return new GpuContext(1024);
    }

    public void TestOutputParameter(out GpuContext ctx)
    {
        ctx = new GpuContext(1024);
    }

    public void TestNullConditionalDisposal(bool condition)
    {
        GpuContext? ctx = condition ? new GpuContext(1024) : null;
        ctx?.Dispose();
    }

    public void TestNullAssignmentToPreventUseAfterFree()
    {
        var ctx = new GpuContext(1024);
        ctx.Dispose();
        ctx = null;
    }
}
