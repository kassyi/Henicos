using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class UsingStaticMathTests : TranspilerTestBase
{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task UsingStaticMath_DirectCall_ReportsGSH0003Once()
    {
        const string source = """
            using System;
            using static System.Math;
            using Kassyi.GSharp.Core;

            public static partial class TestKernelContainer
            {
                [GpuKernel]
                public static void UsingStaticKernel(float[] data)
                {
                    data[ThreadIndex.X] = (float)Sin(data[ThreadIndex.X]);
                }
            }
            """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, s_analyzer);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, diag.Id);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task ExplicitMath_MemberAccessCall_ReportsGSH0003Once()
    {
        const string source = """
            using System;
            using Kassyi.GSharp.Core;

            public static partial class TestKernelContainer
            {
                [GpuKernel]
                public static void ExplicitMathKernel(float[] data)
                {
                    data[ThreadIndex.X] = (float)System.Math.Sin(data[ThreadIndex.X]);
                }
            }
            """;

        var diagnostics = await RoslynTestUtility.GetAnalyzerDiagnosticsAsync(source, s_analyzer);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, diag.Id);
    }
}
