using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for a declaration whose declarators <see cref="ContextHoister"/> partly moves into the context (#1257):
/// the declarators run in the order written.
/// </summary>
[Category(TestCategory.Compiler)]
public class MixedHoistDeclarationLoweringTests
{
    // for (int a = 1, b = a + 1; ...) with 'a' hoisted: 'a' is assigned before 'b' reads it, in front of the loop
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Hoist_ForDeclarationWithLocalAfterHoisted_AssignsHoistedFirst()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "MixedHoistForDemo.cs"));
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method);

        var lowered = AstLoweringPipeline.Lower(TestSyncAnnotator.Annotate(method), null, symbol,
            isDeviceFunction: false);

        var loop = lowered.LoweredBody.DescendantNodes().OfType<ForStatementSyntax>().Single();
        Assert.Null(loop.Declaration);
        Assert.Empty(loop.Initializers);
        var block = Assert.IsType<BlockSyntax>(loop.Parent);
        Assert.Equal(
            [
                "__gsharp_ctx->a = 1;",
                "int b = __gsharp_ctx->a + 1;"
            ],
            block.Statements
                .TakeWhile(static s => s is not ForStatementSyntax)
                .Select(static s => s.NormalizeWhitespace().ToString())
                .ToArray());
    }
}
