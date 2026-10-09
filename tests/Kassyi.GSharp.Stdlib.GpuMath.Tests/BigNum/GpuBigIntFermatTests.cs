using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Common;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntFermatTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Fermat_SmallValues()
    {
        CudaCapability.EnsureGpuAvailable();
        
        using var fermat = new FermatAlgorithm(1); // 2^(2^1)+1 = 5
        var result = fermat.RunFermatPrp();
        Assert.NotNull(result.Result);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Fermat_LargeValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var fermat = new FermatAlgorithm(10); // larger E
        var result = fermat.RunFermatPrp();
        Assert.NotNull(result.Result);
    }
}
