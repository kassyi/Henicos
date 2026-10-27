using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The constructs of a <c>Gpu.SyncGrid(body)</c> that the lowering of a <c>return</c> in the body cannot keep (#626): a
/// declaration after the return that cannot be split into a declaration and an assignment, and a <c>goto</c> that leaves
/// its statement of the body. The analyzer reports them as GSH0003 at the statement.
/// </summary>
[Category(TestCategory.Compiler)]
public class SyncGridBodyDiagnosticsTests
{
    // ref, using, deconstruction, scoped and a pattern variable after the return; not the ref local before it, not the out
    // variables (of an expression statement, a local declaration and an if condition), not a plain local
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnsplittableDeclarationAfterReturn_ReportsGsh0003AtTheStatement(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/SyncGridBodySadPath.cs", dialect);

        Assert.Equal(
            [
                (Analyzer, 45, 13, 45, 36),
                (Analyzer, 46, 13, 46, 51),
                (Analyzer, 47, 13, 47, 45),
                (Analyzer, 50, 13, 50, 42),
                (Analyzer, 51, 13, 51, 40)
            ],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "after a return in a Gpu.SyncGrid body"));
    }

    // goto end leaves its statement; goto next stays in the for statement
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_GotoAcrossTheGaps_ReportsGsh0003AtTheGoto(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync("Analyzers/SyncGridBodySadPath.cs", dialect);

        Assert.Equal(
            [(Analyzer, 64, 27, 64, 36)],
            diagnostics.Ranges(GSharpDiagnostics.UnsupportedSyntax.Id, "goto out of a statement of a Gpu.SyncGrid body"));
    }
}
