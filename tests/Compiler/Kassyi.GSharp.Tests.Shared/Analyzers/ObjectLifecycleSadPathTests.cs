using System.Threading.Tasks;
using Xunit;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

public class ObjectLifecycleSadPathTests : TranspilerTestBase
{
    private static readonly DisposableOwnershipAnalyzer s_ownershipAnalyzer = new();
    private static readonly AllocationAnalyzer s_allocationAnalyzer = new();
    private static readonly SideEffectAnalyzer s_sideEffectAnalyzer = new();
    private const string TestCasePath = "Analyzers/ObjectLifecycleSadPath.cs";

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ObjectLifecycle_MissingIDisposableOnClass_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
            .ReportsErrorsAsync(GSharpDiagnostics.MissingIDisposable);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ObjectLifecycle_UndisposedFieldInDispose_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
            .ReportsErrorsAsync(GSharpDiagnostics.UndisposedGpuScope);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ObjectLifecycle_PropertyGetterSideEffects_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_sideEffectAnalyzer)
            .ReportsErrorsAsync(GSharpDiagnostics.PropertyGetterSideEffect);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ObjectLifecycle_ImplicitCastOperators_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_sideEffectAnalyzer)
            .ReportsErrorsAsync(GSharpDiagnostics.CastOperatorSideEffect);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ObjectLifecycle_UnguardedMultiAllocation_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_allocationAnalyzer)
            .ReportsErrorsAsync(GSharpDiagnostics.UnguardedMultiAllocation);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ObjectLifecycle_StaticLifecycle_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_allocationAnalyzer)
            .ReportsErrorsAsync(GSharpDiagnostics.StaticDisposableAllocation);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ObjectLifecycle_StandardInjectionBorrowing_ReportsHazardDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
            .ReportsErrorsAsync(GSharpDiagnostics.DisposingBorrowedResource);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task ObjectLifecycle_DefaultInterfaceMethods_ReportsDiagnostic()
    {
        await RoslynTestUtility.TestAnalyzer(TestCasePath, s_ownershipAnalyzer)
            .ReportsErrorsAsync(GSharpDiagnostics.DisposableInDim);
    }
}

