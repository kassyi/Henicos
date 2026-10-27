#pragma warning disable CS0219
using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Tier 2 tests for the pre-lowering expansion of <c>Gpu.SyncGrid(body)</c> (#626): the body becomes statements of the
/// method with a <c>Gpu.SyncGrid()</c> between its top-level statements, so that the state machine splits there instead of
/// dropping the body. Without a semantic model, <c>Gpu.SyncGrid</c> is recognized by its syntax.
/// </summary>
[Category(TestCategory.Compiler)]
public class SyncGridExpansionTests : StateMachineTestBase
{
    // case 0: x += 1; yield for grid sync; case 1: x += 2;
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_SyncGrid_TwoStatements_SplitsBetweenThem() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                x += 2;
            });
        });

    // No gap: the statements stay in state 0, and the empty body leaves nothing
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_EmptyOneStatementAndExpressionBody_DoNotSplit() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
            });
            Gpu.SyncGrid(() =>
            {
                x += 1;
            });
            Gpu.SyncGrid(() => x += 2);
        });

    // One grid sync: the explicit one; no automatic sync next to it
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_NextToExplicitSync_SplitsOnce() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                Gpu.SyncGrid();
                x += 2;
            });
        });

    // Empty statements around the explicit sync are skipped when looking for its neighbours
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_ExplicitSyncBetweenEmptyStatements_SplitsOnce() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                ;
                Gpu.SyncGrid();
                ;
                x += 2;
            });
        });

    // x += 1 | x += 2 | x += 3 | x += 4: three grid syncs; a body that only wraps another adds none
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_SyncGrid_NestedBodies_JoinTheGapsOfTheOuterBody() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                Gpu.SyncGrid(() =>
                {
                    x += 2;
                    x += 3;
                });
                x += 4;
            });
            Gpu.SyncGrid(() =>
            {
                Gpu.SyncGrid(() =>
                {
                    x += 500;
                });
            });
        });

    // The for loop is one statement: two grid syncs around it, none inside
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_NestedLoop_IsOneStatement() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                for (int i = 0; i < 10; i++)
                {
                    x += i;
                    x *= 2;
                }
                x += 2;
            });
        });

    // A body inside a loop: the loop is flattened and splits once per iteration
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_SyncGrid_InsideLoop_SplitsEveryIteration() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            for (int k = 0; k < 2; k++)
            {
                Gpu.SyncGrid(() =>
                {
                    x += k;
                    x *= 2;
                });
            }
        });

    // A body in a loop of another body is expanded as well: x += 1 | loop | x += 2 outside, x += k | x *= 2 in each
    // iteration
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_BodyInLoopOfBody_IsExpandedToo() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                for (int k = 0; k < 2; k++)
                {
                    Gpu.SyncGrid(() =>
                    {
                        x += k;
                        x *= 2;
                    });
                }
                x += 2;
            });
        });

    // A local function declaration does not run: one grid sync, between the call and x += 33
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_LocalFunction_DeclarationIsNotAStatementGap() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
                void Step()
                {
                    x += 11;
                }

                Step();
                x += 33;
            });
        });

    // A block barrier in the body is a statement of its own: one block barrier and two grid syncs
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_BlockBarrierInBody_KeepsBothBarriers() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                Gpu.SyncThreads();
                x += 2;
            });
        });

    // A local of the body that crosses a grid sync is hoisted into the context
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_LocalAcrossGaps_IsHoisted() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
                int mine = x + 1;
                x += mine;
                x *= mine;
            });
        });
}
