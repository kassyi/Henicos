using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A <c>throw</c> that device code cannot carry is GSH0003 at the throw (or at the argument that has no device form):
/// an exception that is not created in the throw (<c>throw ex;</c>, <c>throw;</c>), a message that is not a constant,
/// an exception type the host cannot build, and a type that needs an error site in a device function.
/// </summary>
[Category(TestCategory.Compiler)]
public class ThrowDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ThrowForms.cu.cs";

    private static async Task<IReadOnlyList<(int StartLine, int StartColumn, int EndLine, int EndColumn)>> RangesAsync(
        string dialect, string messagePart) =>
        (await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect))
        .Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, messagePart)
        .Select(static r => (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn))
        .Distinct()
        .ToList();

    // throw ex; (line 27)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ThrowOfAVariable_ReportsGsh0003AtTheThrow(string dialect) =>
        Assert.Equal([(27, 13, 27, 22)], await RangesAsync(dialect, ThrowTranspiler.ThrowOfExistingException));

    // throw; is only valid inside a catch clause, which is rejected as a whole at its try keyword (GSH0038, line 37)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Rethrow_InsideACatch_ReportsGsh0038AtTheTry(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(37, 9, 37, 12)],
            diagnostics.Ranges(GSharpDiagnostics.ExceptionHandlingInKernelProhibited.Id)
                .Select(static r => (r.StartLine, r.StartColumn, r.EndLine, r.EndColumn))
                .Distinct());
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NonConstantMessage_ReportsGsh0003AtTheArgument(string dialect) =>
        Assert.Equal([(53, 49, 53, 55)], await RangesAsync(dialect, ThrowTranspiler.ThrowTextNotConstant));

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task TypeTheHostCannotBuild_ReportsGsh0003AtTheThrow(string dialect) =>
        Assert.Equal([(63, 13, 63, 48)], await RangesAsync(dialect, ThrowTranspiler.ThrowTypeNotBuildable));
}
