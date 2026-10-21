namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// Verifies that the generated wrapper of every kernel class gets its own source hint name, derived from the class's
/// full C# path.
/// </summary>
[Category(TestCategory.Compiler)]
public class GeneratorHintNameTests
{
    // Kernel classes with the same name in two namespaces used to be added twice as "K.g.cs", which made the generator
    // throw (CS8785) and contribute no output at all.
    private const string SameNamedKernelClasses = """
        using Kassyi.GSharp.Core;
        namespace N1
        {
            public static partial class K
            {
                [GpuKernel]
                public static void Run(int[] data) { data[ThreadIndex.X] = 1; }
            }
        }
        namespace N2
        {
            public static partial class K
            {
                [GpuKernel]
                public static void Run(int[] data) { data[ThreadIndex.X] = 2; }
            }
        }
        """;

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void Test_SameNamedKernelClassesInDifferentNamespaces_GetDistinctHintNames(string dialect)
    {
        var (driver, output, diagnostics) = RoslynTestUtility.RunGenerator(SameNamedKernelClasses, dialect);
        var result = driver.GetRunResult().Results.Single();

        Assert.Null(result.Exception);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8785");
        Assert.Contains(result.GeneratedSources, s => s.HintName == "N1__K.g.cs");
        Assert.Contains(result.GeneratedSources, s => s.HintName == "N2__K.g.cs");
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_KernelClassInGlobalNamespace_UsesItsName()
    {
        const string source = """
            using Kassyi.GSharp.Core;
            public static partial class TopLevel_Kernels
            {
                [GpuKernel]
                public static void Run(int[] data) { data[ThreadIndex.X] = 1; }
            }
            """;

        var (driver, _, _) = RoslynTestUtility.RunGenerator(source, "Cpu");

        Assert.Contains(driver.GetRunResult().Results.Single().GeneratedSources,
            s => s.HintName == "TopLevel_uKernels.g.cs");
    }
}
