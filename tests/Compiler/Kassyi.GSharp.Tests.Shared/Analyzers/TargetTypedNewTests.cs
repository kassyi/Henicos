using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class TargetTypedNewTests : TranspilerTestBase
{
    private static readonly AllocationAnalyzer s_allocationAnalyzer = new();
    private static readonly SideEffectAnalyzer s_sideEffectAnalyzer = new();

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task StaticDisposableAllocation_TargetTypedNew_Detected()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/TargetTypedNew/StaticDisposableAllocation.cs", s_allocationAnalyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.StaticDisposableAllocation.Id, diag.Id);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task UnguardedMultiAllocation_TargetTypedNew_Detected()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/TargetTypedNew/UnguardedMultiAllocation.cs", s_allocationAnalyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnguardedMultiAllocation.Id, diag.Id);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task PropertyGetterSideEffect_TargetTypedNew_Detected()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/TargetTypedNew/PropertyGetterSideEffect.cs", s_sideEffectAnalyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.PropertyGetterSideEffect.Id, diag.Id);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task CastOperatorSideEffect_TargetTypedNew_Detected()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/TargetTypedNew/CastOperatorSideEffect.cs", s_sideEffectAnalyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.CastOperatorSideEffect.Id, diag.Id);
    }
}
