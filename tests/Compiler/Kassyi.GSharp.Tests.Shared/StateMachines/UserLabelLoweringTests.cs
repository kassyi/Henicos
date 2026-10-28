using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
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
            var text = lowered.LoweredBody.NormalizeWhitespace().ToFullString();
            // The label is state 1: it publishes the value, and the temporary is read in the state after the split
            var labelState = text.IndexOf("case 1:", StringComparison.Ordinal);
            var publish = text.IndexOf("__gsharp_warp_exchange_assign", StringComparison.Ordinal);
            var readState = text.IndexOf("case 2:", StringComparison.Ordinal);
            var temporary = text.IndexOf("var __gsharp_warp_temp_0", StringComparison.Ordinal);
            Assert.True(labelState >= 0 && labelState < publish && publish < readState && readState < temporary, text);
            Assert.Contains("goto case 1;", text);
        });

    // The barrier under the label splits the state machine: the read after it is in a later state than the write
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_LabeledBarrier_SplitsState() =>
        VerifyPipelineLowerFromTestCase("LabeledBarrier.cs", inspect: static (lowered, _) =>
        {
            // The write, the first suspension (the barrier) and the read, in this order
            var text = lowered.LoweredBody.NormalizeWhitespace().ToFullString();
            var write = text.IndexOf("buffer[__gsharp_ctx->tid] =", StringComparison.Ordinal);
            var suspend = text.IndexOf("return;", StringComparison.Ordinal);
            var read = text.IndexOf("output[__gsharp_ctx->tid] =", StringComparison.Ordinal);
            Assert.True(write >= 0 && write < suspend && suspend < read, text);
            Assert.DoesNotContain("SyncThreads", text);
        });

    // No goto reaches the label from another state: one state, and the label and its goto stay as they are
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_LabelLoop_StaysInOneState() =>
        VerifyPipelineLowerFromTestCase("LabelLoop.cs", inspect: static (lowered, _) =>
        {
            var text = lowered.LoweredBody.NormalizeWhitespace().ToFullString();
            Assert.DoesNotContain("case 1:", text);
            Assert.Contains("loop:", text);
            Assert.Contains("goto loop;", text);
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Managed_LabelLoop_StaysInOneState() =>
        VerifyManagedPipelineLowerFromTestCase("LabelLoop.cs", inspect: static (_, fields, body) =>
        {
            Assert.Empty(fields);
            Assert.Contains("goto loop;", body.ToFullString());
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_BranchExitLabel_GotoBecomesGotoCase() =>
        VerifyPipelineLowerFromTestCase("BranchExitLabel.cs", inspect: static (lowered, _) =>
        {
            var text = lowered.LoweredBody.NormalizeWhitespace().ToFullString();
            Assert.DoesNotContain("goto done;", text);
            Assert.DoesNotContain("done:", text);
        });

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_ChainedLabel_BothLabelsStartStates() =>
        VerifyPipelineLowerFromTestCase("ChainedLabel.cs", inspect: static (lowered, _) =>
        {
            var text = lowered.LoweredBody.NormalizeWhitespace().ToFullString();
            Assert.DoesNotContain("goto a;", text);
            Assert.DoesNotContain("goto b;", text);
            Assert.DoesNotContain(" a:", text);
            Assert.DoesNotContain(" b:", text);
        });

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Managed_ChainedLabel_BothLabelsStartStates() =>
        VerifyManagedPipelineLowerFromTestCase("ChainedLabel.cs");

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SameNameLabel_BothLabelsStartStates() =>
        VerifyPipelineLowerFromTestCase("SameNameLabel.cs", inspect: static (lowered, _) =>
        {
            var text = lowered.LoweredBody.NormalizeWhitespace().ToFullString();
            Assert.DoesNotContain("done:", text);
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_CrossLabelWrite_HoistsOnlyLocalsUsedAfterTheLabel() =>
        VerifyPipelineLowerFromTestCase("CrossLabelWrite.cs", inspect: static (lowered, _) =>
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var field in lowered.ContextFields)
            {
                names.Add(field.Identifier.ValueText);
            }

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
