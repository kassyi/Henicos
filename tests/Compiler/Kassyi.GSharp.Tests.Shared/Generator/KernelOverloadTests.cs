namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Verifies that overloaded kernels get distinct entry points and host members, and that overloads whose launch
/// wrappers cannot be told apart are reported instead of breaking the generated code.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelOverloadTests
{
    private static readonly string s_overloadedKernels =
        RoslynTestUtility.ReadTestCase("Generator/KernelOverload_Overloaded.cu.cs");

    // Both overloads take one int buffer in the typed Ptr wrapper.
    private static readonly string s_indistinguishableKernels =
        RoslynTestUtility.ReadTestCase("Generator/KernelOverload_Indistinguishable.cu.cs");

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void Test_OverloadedKernels_GenerateCompilableWrappers(string dialect)
    {
        var (_, output, diagnostics) = RoslynTestUtility.RunGenerator(s_overloadedKernels, dialect);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OverloadedKernels_LoadDistinctEntryPoints()
    {
        var (_, output, _) = RoslynTestUtility.RunGenerator(s_overloadedKernels, "Cuda");
        var wrapper = output.SyntaxTrees.Single(t => t.FilePath.EndsWith("K.g.cs", StringComparison.Ordinal)).ToString();

        Assert.Contains("\"__gsharp_k_OverloadTest__K__Run_o0\"", wrapper);
        Assert.Contains("\"__gsharp_k_OverloadTest__K__Run_o1\"", wrapper);
        Assert.Contains("LaunchRunPtr(global::Kassyi.GSharp.Runtime.GpuBuffer<global::System.Int32>", wrapper);
        Assert.Contains("LaunchRunPtr(global::Kassyi.GSharp.Runtime.GpuBuffer<global::System.Single>", wrapper);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public async Task Test_IndistinguishableOverloads_ReportGsh0059(string dialect)
    {
        // The analyzer reports the later overload at its name; the generator reports nothing
        var diagnostics = await KernelDiagnostics.RunAsync(s_indistinguishableKernels, dialect);
        Assert.Equal(
            [(DiagnosticOrigin.Analyzer, 12, 28, 12, 31)],
            diagnostics.Ranges(GSharpDiagnostics.IndistinguishableKernelOverloads.Id));

        var (_, output, _) = RoslynTestUtility.RunGenerator(s_indistinguishableKernels, dialect);
        // The reported overload gets no wrappers, so the generated code itself stays valid.
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }
}
