namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// When the transpiler throws, the generator reports the internal error (GSH0048) and the analyzer gives up only that
/// method's transpiler failures instead of failing itself (AD0001).
/// </summary>
[Category(TestCategory.Compiler)]
public class TranspilerExceptionTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_TranspilerException_IsAnInternalErrorOfTheGeneratorOnly(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Diagnostics/TranspilerException.cu.cs", dialect);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "AD0001");
        Assert.Contains(diagnostics, static d =>
            d is { Origin: DiagnosticOrigin.Generator, HasLocation: false } && d.Id == GSharpDiagnostics.InternalCompilerError.Id);
    }
}
