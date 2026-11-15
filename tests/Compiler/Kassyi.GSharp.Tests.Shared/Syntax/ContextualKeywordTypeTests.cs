#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Structs named <c>@union</c> and <c>@closed</c> hoisted into the suspension context across <c>Gpu.SyncThreads()</c>
/// compile when parsed as C# 15 (<see cref="LanguageVersion.Preview"/>) and preserve their values across paths.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class ContextualKeywordTypeTests : SpanKernelPathTestBase
{
    private const int ThreadCount = 32;

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void KeywordTypesKernel_ParsedUnderCSharp15Preview_CompilesGeneratedCSharpWithoutErrors(string dialect)
    {
        var kernelPath = RoslynTestUtility.ResolveKernelFilePath();
        var compilation = RoslynTestUtility.CreateCompilationAt(
            LanguageVersion.Preview,
            (kernelPath, File.ReadAllText(kernelPath)));

        var (_, outputCompilation, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(compilation, dialect);

        Assert.Empty(generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        if (dialect == "Cpu")
        {
            Assert.Contains(
                outputCompilation.SyntaxTrees,
                static tree => tree.ToString().Contains(
                                   "public global::Kassyi.GSharp.Tests.Shared.Syntax.@union u;",
                                   StringComparison.Ordinal) &&
                               tree.ToString().Contains(
                                   "public global::Kassyi.GSharp.Tests.Shared.Syntax.@closed c;",
                                   StringComparison.Ordinal));
        }
    }

    [SkippableTheory]
    [MemberData(nameof(SpanWriteTestHelpers.Paths), MemberType = typeof(SpanWriteTestHelpers))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void KeywordTypesKernel_ContextualKeywordStructsAcrossBarrier_PreservesValues(bool managed)
    {
        var output = new int[ThreadCount];

        Run(managed, () => ContextualKeywordTypeKernels.LaunchKeywordTypesKernel(output, blockDimX: ThreadCount));

        for (int t = 0; t < ThreadCount; t++)
        {
            Assert.Equal(t * 3 + 1, output[t]);
        }
    }
}
