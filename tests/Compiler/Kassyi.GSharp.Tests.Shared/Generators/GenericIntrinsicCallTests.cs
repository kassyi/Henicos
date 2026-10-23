using System.Threading.Tasks;
using VerifyXunit;

namespace Kassyi.GSharp.Tests.Shared.Generators;

/// <summary>
/// Verifies the C++ that a kernel gets for calls to a generic intrinsic and to intrinsics of nested classes, declared in
/// the test-only <c>TestCases/NativeApi/GenericIntrinsics.yml</c>.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericIntrinsicCallTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public Task Test_GenericAndNestedIntrinsicCalls_MapToNativeNamesWithTemplateArguments(string dialect)
    {
        var compilation = NativeApiIntrinsicsEmitterTests.CreateGenericIntrinsicsCompilation();

        var (driver, output, diagnostics) = RoslynTestUtility.RunGenerator(compilation, dialect);

        Assert.Empty(diagnostics.Select(d => d.ToString()));
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        var kernels = driver.GetRunResult().Results.Single().GeneratedSources
            .Single(s => s.HintName.EndsWith("GenericIntrinsicCallKernels.g.cs", StringComparison.Ordinal));

        return Verifier.Verify(kernels.SourceText.ToString(), "cs")
            .UseDirectory("Snapshots")
            .UseFileName($"Snap.Test_GenericAndNestedIntrinsicCalls_{dialect}");
    }
}
