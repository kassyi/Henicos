using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// Types and attributes that only share their simple name with a G# one must not be taken for it.
/// </summary>
public class LookalikeSymbolTests
{
    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    private const string DivergentBarrierId = "GSH0037";
    private const string ExternalBarrierCallId = "GSH0042";

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_CoreGpuBarrierInDivergentBranch_IsReported()
    {
        const string source = """
            using Kassyi.GSharp.Core;

            public static class Kernels
            {
                [GpuKernel]
                public static void DivergentKernel(int[] buffer)
                {
                    if (ThreadIndex.X < 16)
                    {
                        Gpu.SyncThreads();
                    }
                }
            }
            """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, s_analyzer);

        Assert.Contains(diagnostics, d => d.Id == DivergentBarrierId);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UserDefinedGpuType_IsNotTakenForTheBarrier()
    {
        const string source = """
            using Kassyi.GSharp.Core;

            namespace Lookalike
            {
                public static class Gpu
                {
                    public static void SyncThreads() { }
                }
            }

            public static class Kernels
            {
                [GpuKernel]
                public static void DivergentKernel(int[] buffer)
                {
                    if (ThreadIndex.X < 16)
                    {
                        Lookalike.Gpu.SyncThreads();
                    }
                }
            }
            """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, s_analyzer);

        Assert.DoesNotContain(diagnostics, d => d.Id == DivergentBarrierId);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UserDefinedGpuKernelAttribute_IsNotTakenForAKernel()
    {
        const string source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace Lookalike
            {
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class GpuKernelAttribute : Attribute { }
            }

            public static class HostCode
            {
                [Lookalike.GpuKernel]
                public static void NotAKernel(int[] buffer)
                {
                    if (ThreadIndex.X < 16)
                    {
                        Gpu.SyncThreads();
                    }
                }
            }
            """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, s_analyzer);

        Assert.DoesNotContain(diagnostics, d => d.Id == DivergentBarrierId);
        Assert.Contains(diagnostics, d => d.Id == ExternalBarrierCallId);
    }
}
