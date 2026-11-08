using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Checks which locals <see cref="ContextHoister"/> moves into the context when one crosses a barrier and another is
/// used after a label that starts a state (<see cref="HoistedVariableAnalysis"/>), and that every context access it
/// writes maps back to the user's identifier (generator-invariants §2).
/// </summary>
[Category(TestCategory.Compiler)]
public class ContextHoisterSpanTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Hoist_BarrierAndSplitLabel_HoistsTheLocalsThatLiveAcrossThem()
    {
        var (lowered, _, _) = Lower();

        // before is used only before the label done and stays a local; extra is used after it
        Assert.Equal(
            ["tid", "value", "extra"],
            lowered.ContextFields.Select(static f => f.Identifier.ValueText)
        );
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Hoist_BarrierAndSplitLabel_ContextAccessesMapToTheUserIdentifiers()
    {
        var (lowered, model, method) = Lower();
        var accesses = lowered.LoweredBody.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(static a => a.IsKind(SyntaxKind.PointerMemberAccessExpression) &&
                               a.Expression is IdentifierNameSyntax
                               {
                                   Identifier.ValueText: GSharpInternalNames.StateMachine.Context
                               } &&
                               a.Name.Identifier.ValueText is "tid" or "value" or "extra")
            .ToArray();

        Assert.NotEmpty(accesses);
        Assert.All(
            accesses,
            access =>
            {
                var location = model.GetOriginalLocation(access.Name);
                Assert.NotNull(location);
                Assert.True(method.Span.Contains(location.SourceSpan), $"'{access}' maps to {location}");
                // An access that replaces a declaration maps to the declarator (<name> = <initializer>)
                Assert.StartsWith(
                    access.Name.Identifier.ValueText,
                    method.SyntaxTree.GetText().ToString(location.SourceSpan),
                    System.StringComparison.Ordinal
                );
            }
        );
    }

    private static (LoweredStateMachine Lowered, SemanticModel Model, MethodDeclarationSyntax Method) Lower()
    {
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "CrossLabelWrite.cs"));
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var symbol = model.GetDeclaredSymbol<IMethodSymbol>(method);
        return (AstLoweringPipeline.Lower(method, model, symbol, isDeviceFunction: false), model, method);
    }
}
