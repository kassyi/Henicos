using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// Compiler and analyzer tests for grid synchronization in non-cooperative kernels, atomic barrier uniformity,
/// and 64-bit atomic code generation.
/// </summary>
[Category(TestCategory.Compiler)]
public class AtomicAndSyncGridDiagnosticsTests
{
    private const string NonCooperativeSyncGridCase = "Diagnostics/NonCooperativeSyncGridSadPath.cs";
    private const string AtomicBarrierUniformityCase = "Diagnostics/AtomicBarrierUniformitySadPath.cs";
    private const string Atomic64AndSharedEmissionCase = "Diagnostics/Atomic64AndSharedEmission.cs";

    // Direct and indirect Gpu.SyncGrid() in a non-cooperative kernel both require [GpuKernel(Cooperative = true)]
    [Theory(Skip = "Pending #694 (GSH0030 indirect reachability)")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task SyncGrid_NonCooperativeKernel_ReportsGsh0030(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(NonCooperativeSyncGridCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 14, 9, 14, 23),
                (Analyzer, 21, 9, 21, 30)
            ],
            diagnostics.Ranges(GSharpDiagnostics.SyncGridRequiresCooperativeKernel.Id));
    }

    // Both Gpu.AtomicAdd and Interlocked.Add return thread-variant values; barriers under conditions on those
    // values must report GSH0083.
    [Theory(Skip = "Pending #1072 (Interlocked return variance and GSH0083)")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task BarrierUniformity_AtomicVsInterlocked_ReportsGsh0083(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AtomicBarrierUniformityCase, dialect);

        Assert.Equal(
            [
                (Analyzer, 13, 13, 13, 29),
                (Analyzer, 27, 13, 27, 29)
            ],
            diagnostics.Ranges(GSharpDiagnostics.LaunchDependentDivergentBarrier.Id));
    }

    // Interlocked.Add(ref long, long) and Interlocked.Exchange(ref long, long) should cast the unsigned atomic result
    // back to a signed integer type in generated C++.
    [Theory(Skip = "Pending #1057 (signed cast on 64-bit Interlocked return)")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("StandardCpp")]
    [InlineData("Cuda")]
    public void InterlockedAdd64Cast_GeneratorRun_EmitsSignedResult(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase(Atomic64AndSharedEmissionCase);
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, dialect);
        string generated = string.Join(
            "\n",
            driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).Select(static s => s.SourceText.ToString()));

        Assert.Contains("(long long)__gsharp_atomicAdd", generated, StringComparison.Ordinal);
        Assert.Contains("(long long)atomicExch", generated, StringComparison.Ordinal);
    }

    // Verify static __shared__ declaration in CUDA C++ has no invalid C++ '= {}' initializer.
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void SharedMemoryDeclaration_CudaGeneratorRun_HasNoInvalidInitializer()
    {
        var source = RoslynTestUtility.ReadTestCase(Atomic64AndSharedEmissionCase);
        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cuda");
        string generated = string.Join(
            "\n",
            driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).Select(static s => s.SourceText.ToString()));

        Assert.Contains("__shared__ int __gsharp_id_2_sBuf[8];", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("__shared__ int __gsharp_id_2_sBuf[8] =", generated, StringComparison.Ordinal);
    }
}
