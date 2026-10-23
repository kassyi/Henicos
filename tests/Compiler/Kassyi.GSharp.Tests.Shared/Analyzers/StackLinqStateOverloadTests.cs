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
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/StackLinqState/StackLinqStateOverloadKernels.cu.cs", s_analyzer)
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
        var diagnostics = await RoslynTestUtility.TestAnalyzer("Analyzers/StackLinqState/StackLinqHostOnlyOperatorKernels.cu.cs", s_analyzer)
                                                 .GetDiagnosticsAsync();
        var diag = Assert.Single(diagnostics);
        Assert.Equal(GSharpDiagnostics.UnsupportedSyntax.Id, diag.Id);
        Assert.Contains("IndexOf", diag.GetMessage());
    }
}
