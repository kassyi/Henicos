using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// The StackLinq overloads that pass a state value to their lambda run on the host only; a kernel reports them.
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
}
