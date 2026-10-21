#nullable enable
using Kassyi.GSharp.Compiler.Dialects;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class RoslynTestUtilityTests
{
    public static int SampleDeviceAdd(int a, int b) => a + b;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TranspileMethod_ForwardsSpecifiedDialect()
    {
        var (cudaOutput, cudaDiagnostics) = RoslynTestUtility.TranspileMethod<RoslynTestUtilityTests>(
            nameof(SampleDeviceAdd),
            isDevice: true,
            dialect: new GSharpCudaDialect());

        var (cppOutput, cppDiagnostics) = RoslynTestUtility.TranspileMethod<RoslynTestUtilityTests>(
            nameof(SampleDeviceAdd),
            isDevice: true,
            dialect: new StandardCppDialect());

        Assert.Empty(cudaDiagnostics);
        Assert.Empty(cppDiagnostics);
        Assert.Contains("__device__", cudaOutput);
        Assert.DoesNotContain("__device__", cppOutput);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CreateDummyContext_ReturnsValidModelAndSymbol()
    {
        var (model, methodSymbol) = RoslynTestUtility.CreateDummyContext();

        Assert.NotNull(model);
        Assert.NotNull(methodSymbol);
        Assert.Equal("M", methodSymbol.Name);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ResolveKernelFilePath_WhenMissing_ThrowsFileNotFoundException()
    {
        const string NonExistentCaller = "NonExistentDummyTests.cs";

        Assert.False(RoslynTestUtility.TryResolveKernelFilePath(NonExistentCaller, out var resolved));
        Assert.Null(resolved);
        Assert.Throws<FileNotFoundException>(() => RoslynTestUtility.ResolveKernelFilePath(NonExistentCaller));
    }
}
