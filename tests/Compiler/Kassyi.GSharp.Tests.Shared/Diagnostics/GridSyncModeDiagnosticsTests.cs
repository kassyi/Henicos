using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The mode of <c>Gpu.SyncGrid(mode)</c> selects the CUDA barrier at compile time: a mode that is not a constant is
/// reported as GSH0012, and a constant that names no member of <c>GridSyncMode</c> as GSH0003, at the argument, on
/// every dialect. The constant spellings (the default, a member, a const, a cast of a member's value) are not
/// reported.
/// </summary>
[Category(TestCategory.Compiler)]
public class GridSyncModeDiagnosticsTests
{
    private const string NotConstantMessage =
        "The argument 'mode' of macro 'Gpu.SyncGrid' must be a compile-time constant";

    private const string UndefinedModeMessage =
        "The syntax 'a GridSyncMode value that names no member of the enum' is not supported in GSharp GPU kernels";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ModeThatIsNotConstant_ReportsGsh0012AtTheArgument(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/GridSyncModeSadPath.cs", dialect);
        string id = GSharpDiagnostics.MacroArgumentMustBeConstant.Id;

        Assert.Equal(
            [
                new ReportedDiagnostic(Analyzer, id, 15, 22, 15, 26, NotConstantMessage),
                new ReportedDiagnostic(Analyzer, id, 16, 22, 16, 27, NotConstantMessage)
            ],
            diagnostics.Where(d => d.Id == id).ToArray());
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_ModeThatNamesNoMember_ReportsGsh0003AtTheArgument(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/GridSyncModeSadPath.cs", dialect);
        string id = GSharpDiagnostics.UnsupportedSyntax.Id;

        Assert.Equal(
            [new ReportedDiagnostic(Analyzer, id, 17, 22, 17, 37, UndefinedModeMessage)],
            diagnostics.Where(d => d.Id == id).ToArray());
    }
}
