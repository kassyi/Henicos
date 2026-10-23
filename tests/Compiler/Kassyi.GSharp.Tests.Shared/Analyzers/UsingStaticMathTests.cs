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
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/UsingStaticMath/UsingStaticMathDirectCallKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, diag.Id);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task ExplicitMath_MemberAccessCall_ReportsGSH0003Once()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/UsingStaticMath/ExplicitMathMemberAccessKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, diag.Id);
    }
}
