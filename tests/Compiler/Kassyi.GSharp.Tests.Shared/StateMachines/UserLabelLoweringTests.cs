using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests of how the state machine lowering treats labels of the user (<c>LabeledStatementSplitter</c>,
/// <c>StateLabelPlanner</c>).
/// </summary>
[Category(TestCategory.Compiler)]
public class UserLabelLoweringTests : StateMachineTestBase
{
    // The label is detached from the shuffle: the shuffle's temporary is declared after the label, in the state that the
    // goto enters
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_LabeledShuffle_TemporaryAfterLabel() =>
        VerifyPipelineLowerFromTestCase("LabeledShuffle.cs", inspect: static (lowered, _) =>
        {
            var case1 = lowered.LoweredBody.DescendantNodes().OfType<CaseSwitchLabelSyntax>()
                .First(static c => c.Value is LiteralExpressionSyntax { Token.ValueText: "1" });
            var publish = lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .First(static i => i.Expression is IdentifierNameSyntax { Identifier.ValueText: "__gsharp_warp_publish" });
            var case2 = lowered.LoweredBody.DescendantNodes().OfType<CaseSwitchLabelSyntax>()
                .First(static c => c.Value is LiteralExpressionSyntax { Token.ValueText: "2" });
            var temp = lowered.LoweredBody.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .First(static v => v.Identifier.ValueText == "__gsharp_warp_temp_0");

            Assert.True(case1.SpanStart < publish.SpanStart &&
                        publish.SpanStart < case2.SpanStart &&
                        case2.SpanStart < temp.SpanStart);
            Assert.True(lowered.LoweredBody.DescendantNodes().OfType<GotoStatementSyntax>()
                .Any(static g => g.CaseOrDefaultKeyword.IsKind(SyntaxKind.CaseKeyword) &&
                                 g.Expression is LiteralExpressionSyntax { Token.ValueText: "1" }));
        });

    // The barrier under the label splits the state machine: the read after it is in a later state than the write
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_LabeledBarrier_SplitsState() =>
        VerifyPipelineLowerFromTestCase("LabeledBarrier.cs", inspect: static (lowered, _) =>
        {
            var write = lowered.LoweredBody.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .First(static a => IsTargetElement(a.Left, "buffer"));
            var suspend = lowered.LoweredBody.DescendantNodes().OfType<ReturnStatementSyntax>()
                .First();
            var read = lowered.LoweredBody.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .First(static a => IsTargetElement(a.Left, "output"));

            Assert.True(write.SpanStart < suspend.SpanStart && suspend.SpanStart < read.SpanStart);
            Assert.False(lowered.LoweredBody.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(static i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SyncThreads" } ||
                                 i.Expression is IdentifierNameSyntax { Identifier.ValueText: "SyncThreads" }));
        });

    private static bool IsTargetElement(ExpressionSyntax left, string name) =>
        left is ElementAccessExpressionSyntax ea &&
        (ea.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText == name,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText == name,
            _ => false,
        });

    // No goto reaches the label from another state: one state, and the label and its goto stay as they are
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_LabelLoop_StaysInOneState() =>
        VerifyPipelineLowerFromTestCase("LabelLoop.cs", inspect: static (lowered, _) =>
        {
            Assert.False(lowered.LoweredBody.DescendantNodes().OfType<CaseSwitchLabelSyntax>()
                .Any(static c => c.Value is LiteralExpressionSyntax { Token.ValueText: "1" }));
            Assert.True(lowered.LoweredBody.DescendantNodes().OfType<LabeledStatementSyntax>()
                .Any(static l => l.Identifier.ValueText == "loop"));
            Assert.True(lowered.LoweredBody.DescendantNodes().OfType<GotoStatementSyntax>()
                .Any(static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: "loop" }));
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Managed_LabelLoop_StaysInOneState() =>
        VerifyManagedPipelineLowerFromTestCase("LabelLoop.cs", inspect: static (_, fields, body) =>
        {
            Assert.Empty(fields);
            Assert.True(body.DescendantNodes().OfType<GotoStatementSyntax>()
                .Any(static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: "loop" }));
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_BranchExitLabel_GotoBecomesGotoCase() =>
        VerifyPipelineLowerFromTestCase("BranchExitLabel.cs", inspect: static (lowered, _) =>
        {
            Assert.False(lowered.LoweredBody.DescendantNodes().OfType<GotoStatementSyntax>()
                .Any(static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: "done" }));
            Assert.False(lowered.LoweredBody.DescendantNodes().OfType<LabeledStatementSyntax>()
                .Any(static l => l.Identifier.ValueText == "done"));
        });

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_ChainedLabel_BothLabelsStartStates() =>
        VerifyPipelineLowerFromTestCase("ChainedLabel.cs", inspect: static (lowered, _) =>
        {
            var gotos = lowered.LoweredBody.DescendantNodes().OfType<GotoStatementSyntax>();
            Assert.False(gotos.Any(static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: "a" or "b" }));
            var labels = lowered.LoweredBody.DescendantNodes().OfType<LabeledStatementSyntax>();
            Assert.False(labels.Any(static l => l.Identifier.ValueText is "a" or "b"));
        });

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Managed_ChainedLabel_BothLabelsStartStates() =>
        VerifyManagedPipelineLowerFromTestCase("ChainedLabel.cs");

    // Two labels of one name split no state: the only states are the two barriers and the end
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SameNameLabel_SplitsOnlyAtBarriers() =>
        VerifyPipelineLowerFromTestCase("SameNameLabel.cs", inspect: static (lowered, _) =>
        {
            var doneLabels = lowered.LoweredBody.DescendantNodes().OfType<LabeledStatementSyntax>()
                .Count(static l => l.Identifier.ValueText == "done");
            Assert.Equal(2, doneLabels);
            var cases = lowered.LoweredBody.DescendantNodes().OfType<CaseSwitchLabelSyntax>();
            Assert.True(cases.Any(static c => c.Value is LiteralExpressionSyntax { Token.ValueText: "2" }));
            Assert.False(cases.Any(static c => c.Value is LiteralExpressionSyntax { Token.ValueText: "3" }));
        });

    // The managed output renames the two labels and the gotos to them, each goto to the label it binds to in C#
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Managed_SameNameLabel_RenamesLabelsAndGotos() =>
        VerifyManagedPipelineLowerFromTestCase("SameNameLabel.cs", inspect: static (_, _, body) =>
        {
            Assert.False(body.DescendantNodes().OfType<LabeledStatementSyntax>()
                .Any(static l => l.Identifier.ValueText == "done"));

            var goto1 = body.DescendantNodes().OfType<GotoStatementSyntax>()
                .First(static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: "__gsharp_label_1_done" });
            var label1 = body.DescendantNodes().OfType<LabeledStatementSyntax>()
                .First(static l => l.Identifier.ValueText == "__gsharp_label_1_done");
            var goto2 = body.DescendantNodes().OfType<GotoStatementSyntax>()
                .First(static g => g.Expression is IdentifierNameSyntax { Identifier.ValueText: "__gsharp_label_2_done" });
            var label2 = body.DescendantNodes().OfType<LabeledStatementSyntax>()
                .First(static l => l.Identifier.ValueText == "__gsharp_label_2_done");

            Assert.True(goto1.SpanStart < label1.SpanStart);
            Assert.True(label1.SpanStart < goto2.SpanStart);
            Assert.True(goto2.SpanStart < label2.SpanStart);
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_CrossLabelWrite_HoistsOnlyLocalsUsedAfterTheLabel() =>
        VerifyPipelineLowerFromTestCase("CrossLabelWrite.cs", inspect: static (lowered, _) =>
        {
            var names = lowered.ContextFields.Select(static f => f.Identifier.ValueText).ToArray();

            Assert.Contains("extra", names);
            Assert.DoesNotContain("before", names);
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Managed_CrossLabelWrite_HoistsOnlyLocalsUsedAfterTheLabel() =>
        VerifyManagedPipelineLowerFromTestCase("CrossLabelWrite.cs", inspect: static (_, fields, _) =>
        {
            Assert.Contains("extra", fields);
            Assert.DoesNotContain("before", fields);
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Managed_LabeledShuffle_TemporaryAfterLabel() =>
        VerifyManagedPipelineLowerFromTestCase("LabeledShuffle.cs");
}
