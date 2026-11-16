#pragma warning disable CS0219
using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for pre-lowering expansion of <c>Gpu.ForUnroll</c> statement macros that contain phase barriers (#559).
/// </summary>
[Category(TestCategory.Compiler)]
public class ForUnrollExpansionTests : StateMachineTestBase
{
    private struct TestAcc
    {
        public int Total;
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_ForUnroll_WithBarrier_ExpandsToForAndSplitsStateMachine() =>
        VerifyPipelineLower(() =>
        {
            int sum = 0;
            Gpu.ForUnroll(0, 3, iter =>
            {
                sum += iter;
                Gpu.SyncThreads();
            });
        });

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_ForUnroll_WithoutBarrier_RemainsMacroCall() =>
        VerifyPipelineLower(() =>
        {
            int sum = 0;
            Gpu.ForUnroll(0, 3, iter =>
            {
                sum += iter;
            });
            Gpu.SyncThreads();
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_ForUnroll_WithRefContext_SubstitutesContextArgument() =>
        VerifyPipelineLower(() =>
        {
            TestAcc acc = default;
            Gpu.ForUnroll(ref acc, 1, 3, (ref a, i) =>
            {
                a.Total += i;
                Gpu.SyncThreads();
            });
        });

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_ForUnroll_Nested_ExpandsOnlyOuterWithBarrier() =>
        VerifyPipelineLower(() =>
        {
            int sum = 0;
            Gpu.ForUnroll(0, 2, o =>
            {
                Gpu.SyncThreads();
                Gpu.ForUnroll(0, 3, j =>
                {
                    sum += o * 10 + j;
                });
            });
        });

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_ForUnroll_TwoInSameMethod_UsesDistinctLoopCounters() =>
        VerifyPipelineLower(() =>
        {
            int sum = 0;
            Gpu.ForUnroll(0, 2, a =>
            {
                sum += a;
                Gpu.SyncThreads();
            });
            Gpu.ForUnroll(1, 3, b =>
            {
                sum += b;
                Gpu.SyncThreads();
            });
        });
}
