#pragma warning disable CS0219, CS0168, CS0164
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
    // done flag at the top; the return sets it and jumps past its statement; the later statements are guarded and the
    // syncs stay outside the guards
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_SyncGrid_Return_SkipsTheRestButKeepsTheSyncs() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            int c = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                if (c > 0) return;
                x += 2;
                x += 3;
            });
        });

    // The nested body has its own flag; x += 3 of the outer body is not guarded
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_ReturnInNestedBody_GuardsTheNestedRestOnly() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            int c = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                Gpu.SyncGrid(() =>
                {
                    if (c > 0) return;
                    x += 2;
                });
                x += 3;
            });
        });

    // A return of a lambda or a local function in the body is not rewritten, and adds no flag
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_ReturnOfLambdaOrLocalFunction_IsNotRewritten() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                Gpu.ForUnroll(0, 2, i =>
                {
                    if (i == 1) return;
                    x += i;
                });
                void Step()
                {
                    return;
                }

                Step();
            });
        });

    // Declarations after the return move to the top of the body; the guards keep only the assignments; const stays.
    // Without a semantic model (as here) a var declaration would move as var <name>; with one, the type is written out
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_DeclarationsAfterReturn_MoveToTheTop() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            int c = 0;
            Gpu.SyncGrid(() =>
            {
                if (c > 0) return;
                int v = x;
                int p = 1, q = p;
                int u;
                const int k = 2;
                x += v + q + k;
            });
        });

    // The out variables of guarded statements move to the top of the body and are passed as out <name>
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_OutVariablesAfterReturn_MoveToTheTop() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            int c = 0;
            Gpu.SyncGrid(() =>
            {
                if (c > 0) return;
                Read(out int v);
                if (Read(out int w) > 0) x += w;
                x += v;
            });
        });

    // The return inside the user's switch jumps to the label of the next state, not to a goto case that the user's switch
    // would bind (here to its case 2)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_ReturnInSwitch_JumpsToTheLabelOfTheState() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            int c = 0;
            Gpu.SyncGrid(() =>
            {
                x += 1;
                switch (c)
                {
                    case 0:
                        return;
                    case 2:
                        x += 100;
                        break;
                    default:
                        break;
                }
                x += 2;
            });
        });

    // A return in a foreach statement and in a statement with a user label
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_ReturnInForEachAndLabeledStatement_JumpsToTheirLabels() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            int[] values = [1, 2];
            Gpu.SyncGrid(() =>
            {
                foreach (int value in values)
                {
                    if (value == x) return;
                }
                Check: if (x > 3) return;
                x += 2;
            });
        });

    // The outer return guards the nested body; the last nested statement needs both flags clear
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SyncGrid_NestedReturnAfterReturn_GuardsWithBothFlags() =>
        VerifyPipelineLower(() =>
        {
            int x = 0;
            int c = 0;
            Gpu.SyncGrid(() =>
            {
                if (c > 0) return;
                Gpu.SyncGrid(() =>
                {
                    if (c > 1) return;
                    x += 1;
                });
                x += 2;
            });
        });

    private static int Read(out int value)
    {
        value = 1;
        return 1;
    }
}
