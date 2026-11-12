using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// StackLinq operators that run on the host only (state overloads, AddTo, Do, SelectMany, TryFirst, IndexOf ...);
/// a kernel reports them.
/// </summary>
public class StackLinqStateOverloadTests : TranspilerTestBase
{
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task StateOverload_InKernel_ReportsGSH0003Once()
    {
        var diagnostics = await GeneratorTestRunner.TestAnalyzer("Analyzers/StackLinqState/StackLinqStateOverloadKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, diag.Id);
        Assert.Contains("Count", diag.GetMessage());
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task HostOnlyOperator_InKernel_ReportsGSH0003Once()
    {
        var diagnostics = await GeneratorTestRunner.TestAnalyzer("Analyzers/StackLinqState/StackLinqHostOnlyOperatorKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, diag.Id);
        Assert.Contains("IndexOf", diag.GetMessage());
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task OfType_InKernel_ReportsGSH0003Once()
    {
        var diagnostics = await GeneratorTestRunner.TestAnalyzer("Analyzers/StackLinqState/StackLinqOfTypeKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, diag.Id);
        Assert.Contains("OfType", diag.GetMessage());
    }

    // #678: Sum over a struct adds with its user-defined +, which GPU code does not call; the fused chain and the query
    // stored in a local are each reported at their Sum call instead of failing the C++ build
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task SumOverUserStruct_InKernel_ReportsGSH0003AtEachSum()
    {
        var diagnostics = await GeneratorTestRunner.TestAnalyzer("Analyzers/StackLinqState/StackLinqStructSumKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();
        var errors = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id)
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .ToArray();

        // mutant: StackLinqHelper.TryGetUnsupportedElementType accepting every Sum element type (no diagnostic)
        Assert.Equal(2, errors.Length);
        Assert.All(errors, static d => Assert.Contains("Sum over 'StackLinqMoney'", d.GetMessage()));
        Assert.Equal(22, errors[0].Location.GetLineSpan().StartLinePosition.Line);
        Assert.Equal(24, errors[1].Location.GetLineSpan().StartLinePosition.Line);
    }

    // Contains over a struct compares with EqualityComparer<T>.Default, which the device does not reproduce; reported
    // at each call instead of failing the C++ build
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task ContainsOverUserStruct_InKernel_ReportsGSH0003AtEachContains()
    {
        var diagnostics = await GeneratorTestRunner.TestAnalyzer("Analyzers/StackLinqState/StackLinqStructContainsKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();
        var errors = diagnostics
            .Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id)
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .ToArray();

        // mutant: StackLinqHelper.TryGetUnsupportedElementType without the Contains case (no diagnostic)
        Assert.Equal(2, errors.Length);
        Assert.All(errors, static d => Assert.Contains("Contains over 'StackLinqPoint'", d.GetMessage()));
        Assert.Equal(17, errors[0].Location.GetLineSpan().StartLinePosition.Line);
        Assert.Equal(19, errors[1].Location.GetLineSpan().StartLinePosition.Line);
    }

    // #1081: an operator that the device StackLinq header does not define is reported at its call
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public async Task CopyTo_InKernel_ReportsGSH0003Once()
    {
        var diagnostics = await GeneratorTestRunner.TestAnalyzer("Analyzers/StackLinqState/StackLinqCopyToKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, diag.Id);
        Assert.Contains("CopyTo", diag.GetMessage());
    }
}
