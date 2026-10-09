using System;
using System.Linq;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Tests.Common;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

public class GpuBigIntDivRemTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;

    public GpuBigIntDivRemTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DivRem_Reduce_InverseSqrt_Heron()
    {
        using var customCtx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigUInt(customCtx, new uint[] { 4000000000, 4000000000, 4000000000, 4000000000 });
        using var b = new GpuBigUInt(customCtx, new uint[] { 2, 0, 0, 0 });
        using var q = new GpuBigUInt(customCtx, 4);
        using var r = new GpuBigUInt(customCtx, 4);

        // Divide
        GpuBigIntDivRem.DivRem(a, b, q, r);

        // Reciprocal
        using var recip = new GpuBigUInt(customCtx, 4);
        GpuBigIntDivRem.Reciprocal(a, recip, 128);

        // InverseSqrt
        using var invSqrt = new GpuBigUInt(customCtx, 4);
        GpuBigIntDivRem.InverseSqrt(a, invSqrt, 0);

        // Sqrt
        using var sqrt = new GpuBigUInt(customCtx, 4);
        GpuBigIntDivRem.Sqrt(a, sqrt);
    }
}
