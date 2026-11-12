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
    // attributes). Not reported: the parameters of Passable (a launch setting of a type that converts to uint and an
    // IntPtr stream included), of Gridded (dimensions that convert through a user-defined operator), of Called
    // (function pointers), of Many (a params array, a name that starts with __gsharp_), and the 'ref', 'out', 'in' and
    // 'ref readonly' values, multidimensional and nullable arrays of Counted, Written and Packed
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Parameters_LauncherCannotPass_ReportGSH0110OverEachParameter()
    {
        var diagnostics = await GeneratorTestRunner.TestAnalyzer(TestCase, s_analyzer).GetDiagnosticsAsync();

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
                (Id, 31, 39, 31, 58),
                (Id, 31, 60, 31, 73),
                (Id, 31, 75, 31, 91),
                (Id, 31, 93, 31, 106),
                (Id, 34, 45, 34, 61),
                (Id, 37, 38, 37, 63),
                (Id, 52, 44, 52, 60),
                (Id, 52, 62, 52, 78),
                (Id, 52, 80, 52, 95),
                (Id, 52, 97, 52, 114),
                (Id, 53, 9, 53, 30),
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
    [InlineData("pair", "Paired", "(byte, double)", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("auto", "Paired", "AutoPair", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("holder", "Paired", "HoldsAuto", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("time", "Paired", "DateTime", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("items", "AutoElements", "AutoPair[]", ModuleKernelParameterTypes.AutoLayoutReason)]
    [InlineData("values", "Spans", "Span<float>", ModuleKernelParameterTypes.RefStructReason)]
    [InlineData("data", "ByReference", "float[]", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("label", "ByReference", "string", ModuleKernelParameterTypes.ManagedTypeReason)]
    [InlineData("counts", "ByReference", "int*", ModuleKernelParameterTypes.ByReferenceReason)]
    [InlineData("gridDimX", "ByReference", "uint", ModuleKernelParameterTypes.LaunchSettingByReferenceReason)]
    [InlineData("autoPair", "ByReference", "AutoPair", ModuleKernelParameterTypes.AutoLayoutReason)]
    public async Task Parameter_LauncherCannotPass_NamesParameterMethodTypeAndReason(
        string parameter,
        string method,
        string type,
        string reason)
    {
        var diagnostics = await GeneratorTestRunner.TestAnalyzer(TestCase, s_analyzer).GetDiagnosticsAsync();

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
