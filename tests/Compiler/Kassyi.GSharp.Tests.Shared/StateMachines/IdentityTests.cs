using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Dialects;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
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
    [Scenario(TestScenario.HappyPath)]
    public Task Managed_DupDemo_DistinguishesContextFieldsPerRegion() =>
        VerifyManagedPipelineLowerFromTestCase(
            "DupDemo.cs",
            static (annotatedBody, model) =>
            {
                var tidDecl = annotatedBody.Statements[0];
                var innerBlock = (BlockSyntax)annotatedBody.Statements[1];
                var copy1 = LoweringAnnotations.StampRegion(innerBlock, region: 1, model);
                var copy2 = LoweringAnnotations.StampRegion(innerBlock, region: 2, model);
                return annotatedBody.WithStatements(SyntaxFactory.List([tidDecl, copy1, copy2]));
            },
            static (localInitNames, fieldNames, _) =>
            {
                Assert.Empty(localInitNames);
                Assert.Equal(["tid", "__gsharp_il_1_2_s", "__gsharp_il_2_2_s"], fieldNames);
            });

    // A ref struct local is declared again on every resume instead of living in the context class
    // (ManagedContextFields.LocalInits). Its copies need their own local names, the declarator-derived initialization
    // of each copy is removed, and the reassignment after the barrier stays.
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Managed_DupSpanDemo_DistinguishesRefStructLocalsPerRegion() =>
        VerifyManagedPipelineLowerFromTestCase(
            "DupSpanDemo.cs",
            static (annotatedBody, model) =>
            {
                var tidDecl = annotatedBody.Statements[0];
                var innerBlock = (BlockSyntax)annotatedBody.Statements[1];
                var copy1 = LoweringAnnotations.StampRegion(innerBlock, region: 1, model);
                var copy2 = LoweringAnnotations.StampRegion(innerBlock, region: 2, model);
                return annotatedBody.WithStatements(SyntaxFactory.List([tidDecl, copy1, copy2]));
            },
            static (localInitNames, fieldNames, body) =>
            {
                Assert.Equal(["__gsharp_il_1_3_view", "__gsharp_il_2_3_view"], localInitNames);
                Assert.Equal(["tid", "__gsharp_il_1_2_s", "__gsharp_il_2_2_s"], fieldNames);

                // No context access to a ref struct local is left; every reference is the bare local name
                Assert.DoesNotContain(
                    body.DescendantNodes().OfType<MemberAccessExpressionSyntax>(),
                    static m => ContextLocalRewriter.IsContextPointerAccess(m) &&
                                m.Name.Identifier.ValueText.EndsWith("view", StringComparison.Ordinal));

                // The initialization `view = output` of each copy is removed; the reassignment after the barrier stays
                var viewAssignments = body.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Select(static a => a.Left)
                    .OfType<IdentifierNameSyntax>()
                    .Select(static name => name.Identifier.ValueText)
                    .Where(static name => name.EndsWith("view", StringComparison.Ordinal))
                    .ToArray();
                Assert.Equal(["__gsharp_il_1_3_view", "__gsharp_il_2_3_view"], viewAssignments);

                var viewReads = body.DescendantNodes()
                    .OfType<ElementAccessExpressionSyntax>()
                    .Select(static e => e.Expression)
                    .OfType<IdentifierNameSyntax>()
                    .Select(static name => name.Identifier.ValueText)
                    .ToArray();
                Assert.Equal(["__gsharp_il_1_3_view", "__gsharp_il_2_3_view"], viewReads);
            });

    // Checks the IteratorInliner half of the boundary: seeded with RegionCount, the highest region a pre-lowering
    // expansion used, an inline iterator expansion takes the next number. The seeding itself
    // (MethodTranspiler.GenerateCudaSource sets Emitter.IteratorExpansionCounter from LoweredStateMachine.RegionCount)
    // is checked when PreLoweringExpander starts to set RegionCount.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IteratorExpansion_AfterTwoPreLoweringRegions_TakesTheNextRegion()
    {
        const int RegionCount = 2;
        var (_, tree, model) = RoslynTestUtility.GetTestCaseContext(
            Path.Combine("StateMachines", "RegionCounterIterator.cs"));
        var forEach = tree.GetRoot()
            .DescendantNodes()
            .OfType<ForEachStatementSyntax>()
            .Single();
        var invocation = Assert.IsType<InvocationExpressionSyntax>(forEach.Expression);
        var iteratorMethod = Assert.IsAssignableFrom<IMethodSymbol>(model.GetSymbolInfo(invocation).Symbol);
        var failures = new List<TranspileFailure>();

        var regionCounter = RegionCount;
        var argumentTempCounter = 0;
        var expanded = IteratorInliner.Expand(
            forEach,
            iteratorMethod,
            invocation,
            model,
            ref regionCounter,
            ref argumentTempCounter,
            new StandardCppDialect(),
            new Reporter(failures.Add, model));

        Assert.Empty(failures);
        var endLabel = Assert.IsType<LabeledStatementSyntax>(expanded.Statements[1]);
        Assert.Equal($"{GSharpInternalNames.Iterator.EndLabelPrefix}{RegionCount + 1}", endLabel.Identifier.ValueText);
        var stampedRegions = expanded.DescendantNodes()
            .Select(LoweringIdentity.GetRegion)
            .Where(static region => region > 0)
            .Distinct()
            .ToArray();
        Assert.NotEmpty(stampedRegions);
        Assert.All(stampedRegions, static region => Assert.True(region > RegionCount));
    }

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
