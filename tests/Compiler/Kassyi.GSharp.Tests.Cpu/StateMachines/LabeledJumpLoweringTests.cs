using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests of how the state machine lowering treats a labeled <c>break</c> or <c>continue</c> whose target loop or
/// <c>switch</c> holds a split point (<c>LabeledJumpMarker</c>, <c>LabeledJumpRewriter</c>, #1990).
/// </summary>
[Category(TestCategory.Compiler)]
public class LabeledJumpLoweringTests : StateMachineTestBase
{
    // The label is next to the flattened loop's own labels, and no jump keeps the name of a label that was detached
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_LabeledJumpsOfABarrierLoop_BecomeGotos() =>
        VerifyPipelineLowerFromTestCase("LabeledJumpBarrierLoop.cs", inspect: static (lowered, _) =>
        {
            Assert.False(lowered.LoweredBody.DescendantNodes().OfType<BreakStatementSyntax>().Any());
            Assert.False(lowered.LoweredBody.DescendantNodes().OfType<ContinueStatementSyntax>().Any());
        });

    // A section of a switch that holds a barrier is flattened too: its jumps to the loop around it are gotos as well
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_LabeledJumpsOfASwitchSection_BecomeGotos() =>
        VerifyPipelineLowerFromTestCase("LabeledJumpBarrierSwitch.cs", inspect: static (lowered, _) =>
        {
            Assert.False(lowered.LoweredBody.DescendantNodes().OfType<BreakStatementSyntax>().Any());
            Assert.False(lowered.LoweredBody.DescendantNodes().OfType<ContinueStatementSyntax>().Any());
        });
}
