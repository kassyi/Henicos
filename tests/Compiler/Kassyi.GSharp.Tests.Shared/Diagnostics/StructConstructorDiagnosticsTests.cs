using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// <c>new S(args)</c> becomes a factory function or a mapped device constructor; a constructor that is neither is
/// reported (GSH0003) at the creation instead of becoming aggregate initialization (#499).
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class StructConstructorDiagnosticsTests
{
    private const string TestCase = "Diagnostics/StructConstructorUnsupported.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_VectorConstructorWithoutMapping_ReportsGsh0003AtTheCreation(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // mutant: ObjectCreationHelper.Classify `: StructCreationKind.Unsupported` -> `: StructCreationKind.Braces` -> no
        // GSH0003 (every test of this class fails)
        Assert.Equal(
            [(Analyzer, 14, 17, 14, 56)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "constructor 'System.Numerics.Vector3(System.ReadOnlySpan<float>)' without a device equivalent"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ConstructorFromAnotherAssembly_ReportsGsh0003AtTheCreation(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 22, 23, 22, 82)],
            diagnostics.Ranges(
                GSharpDiagnostics.UnsupportedSyntax.Id,
                "constructor 'Kassyi.GSharp.Core.GpuThreadContext(int, int, int, int, int, int, int, int, int, int, int, int)' without a device equivalent"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_OnlyTheUnsupportedConstructorsAreReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(2, diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id).Count);
    }
}
