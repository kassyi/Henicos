using Kassyi.GSharp.Compiler.Diagnostics;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class UnrollControlFlowScopeTests : TranspilerTestBase
{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_UnrollControlFlowScope_HappyPath_HasNoErrors()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/UnrollControlFlowScope_HappyPath.cs", s_analyzer)
                               .HasNoErrorsAsync();
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnrollControlFlowScope_SadPath_ReportsExpectedDiagnostics()
    {
        await RoslynTestUtility.TestAnalyzer("Analyzers/UnrollControlFlowScope_SadPath.cs", s_analyzer)
                               .ReportsDiagnosticsAsync(new[]
                               {
                                   GSharpDiagnostics.UnrolledLoopMayBeIgnored.Id,
                                   GSharpDiagnostics.UnrolledLoopMayBeIgnored.Id,
                                   GSharpDiagnostics.UnrolledLoopMayBeIgnored.Id
                               });
    }
}
