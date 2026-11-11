using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.NativeApi;

#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CudaImportAnalyzerTests
{
    private const string TestCase = "NativeApi/CudaImportParameterDiagnostics.cs";

    private static readonly CudaImportAnalyzer s_analyzer = new();

    // Each parameter the launcher cannot pass is reported over its modifiers, type and name (without its
    // attributes); the parameters of Passable, a launch setting of a type that converts to uint and an IntPtr stream
    // included, are not
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Parameters_LauncherCannotPass_ReportGSH0110OverEachParameter()
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer(TestCase, s_analyzer).GetDiagnosticsAsync();

        var actual = diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ToArray();
        const string Id = "GSH0110";
        Assert.Equal(
            [
                (Id, 13, 37, 13, 48),
                (Id, 16, 41, 16, 63),
                (Id, 19, 42, 19, 56),
                (Id, 22, 55, 22, 65),
                (Id, 25, 41, 25, 51),
                (Id, 28, 40, 28, 53),
                (Id, 28, 55, 28, 68),
            ],
            actual);
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("text", "Text", "string", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("callback", "Callback", "Action", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("rows", "Rows", "float[][]", ModuleKernelParameterTypes.JaggedArrayReason)]
    [InlineData("stream", "Streamed", "int", ModuleKernelParameterTypes.StreamTypeReason)]
    [InlineData("count", "Optional", "int?", ModuleKernelParameterTypes.NullableValueReason)]
    [InlineData("grid", "Counted", "float[,]", ModuleKernelParameterTypes.MultidimensionalArrayReason)]
    public async Task Parameter_LauncherCannotPass_NamesParameterMethodTypeAndReason(
        string parameter,
        string method,
        string type,
        string reason)
    {
        var diagnostics = await RoslynTestUtility.TestAnalyzer(TestCase, s_analyzer).GetDiagnosticsAsync();

        var message = Assert.Single(
            diagnostics,
            d => d.GetMessage().StartsWith($"Parameter '{parameter}' ", StringComparison.Ordinal)
        ).GetMessage();
        Assert.Equal(
            $"Parameter '{parameter}' of [CudaImport] method 'ImportedKernels.{method}' has type '{type}', " +
            $"which the launcher cannot pass to the kernel: {reason}",
            message);
    }
}
