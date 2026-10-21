namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Verifies that overloaded kernels get distinct entry points and host members, and that overloads whose launch
/// wrappers cannot be told apart are reported instead of breaking the generated code.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelOverloadTests
{
    private const string OverloadedKernels = """
        using Kassyi.GSharp.Core;
        namespace OverloadTest
        {
            public static partial class K
            {
                [GpuKernel]
                public static void Run(int[] data) { data[ThreadIndex.X] = 1; }

                [GpuKernel]
                public static void Run(float[] data) { data[ThreadIndex.X] = 2f; }
            }
        }
        """;

    // Both overloads take one int buffer in the typed Ptr wrapper.
    private const string IndistinguishableKernels = """
        using Kassyi.GSharp.Core;
        namespace OverloadTest
        {
            public static partial class K
            {
                [GpuKernel]
                public static void Run(int[] data) { data[ThreadIndex.X] = 1; }

                [GpuKernel]
                public static void Run(ref int value) { value = 2; }
            }
        }
        """;

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void Test_OverloadedKernels_GenerateCompilableWrappers(string dialect)
    {
        var (_, output, diagnostics) = RoslynTestUtility.RunGenerator(OverloadedKernels, dialect);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OverloadedKernels_LoadDistinctEntryPoints()
    {
        var (_, output, _) = RoslynTestUtility.RunGenerator(OverloadedKernels, "Cuda");
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
    public void Test_IndistinguishableOverloads_ReportGsh0061(string dialect)
    {
        var (_, output, diagnostics) = RoslynTestUtility.RunGenerator(IndistinguishableKernels, dialect);

        diagnostics.AssertContains(GSharpDiagnostics.IndistinguishableKernelOverloads);
        // The reported overload gets no wrappers, so the generated code itself stays valid.
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }
}
