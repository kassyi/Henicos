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
            var text = lowered.LoweredBody.NormalizeWhitespace().ToFullString();
            Assert.Contains("__gsharp_state = 2;", text);
            Assert.DoesNotContain("Gpu.SyncThreads()", text);
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Managed_LabeledShuffle_TemporaryAfterLabel() =>
        VerifyManagedPipelineLowerFromTestCase("LabeledShuffle.cs");
}
