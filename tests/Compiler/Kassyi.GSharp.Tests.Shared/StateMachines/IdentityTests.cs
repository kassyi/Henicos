using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 state-machine lowering tests for duplicated regions (<see cref="LoweringAnnotations.StampRegion{TNode}"/>)
/// and <see cref="DeclarationKey"/> identity across <see cref="ContextHoister"/> and <see cref="StateMachineLowerer"/>.
/// </summary>
[Category(TestCategory.Compiler)]
public class IdentityTests : StateMachineTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Pipeline_DupDemo_DistinguishesHoistedFieldsAndLabelsPerRegion() =>
        VerifyPipelineLowerFromTestCase(
            "DupDemo.cs",
            static (annotatedBody, model) =>
            {
                var tidDecl = annotatedBody.Statements[0];
                var innerBlock = (BlockSyntax)annotatedBody.Statements[1];
                var copy1 = LoweringAnnotations.StampRegion(innerBlock, region: 1, model);
                var copy2 = LoweringAnnotations.StampRegion(innerBlock, region: 2, model);
                return annotatedBody.WithStatements(SyntaxFactory.List([tidDecl, copy1, copy2]));
            });

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Pipeline_NestedDupDemo_PreservesOuterRegionOnOuterReferences() =>
        VerifyPipelineLowerFromTestCase(
            "NestedDupDemo.cs",
            static (annotatedBody, model) =>
            {
                var tidDecl = annotatedBody.Statements[0];
                var outerBlock = (BlockSyntax)annotatedBody.Statements[1];

                var outerCopy1 = LoweringAnnotations.StampRegion(outerBlock, region: 1, model);
                var innerBlock1 = (BlockSyntax)outerCopy1.Statements[1];
                var innerCopy1A = LoweringAnnotations.StampRegion(innerBlock1, region: 3, model);
                var innerCopy1B = LoweringAnnotations.StampRegion(innerBlock1, region: 4, model);
                outerCopy1 = outerCopy1.WithStatements(
                    SyntaxFactory.List([outerCopy1.Statements[0], innerCopy1A, innerCopy1B]));

                var outerCopy2 = LoweringAnnotations.StampRegion(outerBlock, region: 2, model);
                var innerBlock2 = (BlockSyntax)outerCopy2.Statements[1];
                var innerCopy2A = LoweringAnnotations.StampRegion(innerBlock2, region: 5, model);
                var innerCopy2B = LoweringAnnotations.StampRegion(innerBlock2, region: 6, model);
                outerCopy2 = outerCopy2.WithStatements(
                    SyntaxFactory.List([outerCopy2.Statements[0], innerCopy2A, innerCopy2B]));

                return annotatedBody.WithStatements(SyntaxFactory.List([tidDecl, outerCopy1, outerCopy2]));
            },
            static (lowered, _) =>
            {
                var outerRefs = lowered.LoweredBody.DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Where(static m => ContextLocalRewriter.IsContextPointerAccess(m) &&
                                       m.Name.Identifier.ValueText == "outerLocal")
                    .Select(static m => LoweringIdentity.GetRegion(m.Name))
                    .ToArray();

                // Each outer copy has 1 declaration assignment + 2 inner copies * (1 initializer read + 1 post-barrier read) = 5 accesses.
                Assert.Equal([1, 1, 1, 1, 1, 2, 2, 2, 2, 2], outerRefs);
            });
}
