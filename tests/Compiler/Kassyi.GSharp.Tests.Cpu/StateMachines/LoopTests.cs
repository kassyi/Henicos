#pragma warning disable CS0219
using System.Threading.Tasks;
using Xunit;


namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class LoopTests : StateMachineTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Lower_WhileStatement()
    {
        bool condition = true;
        return VerifyLoopRewrite(() =>
        {
            while (condition)
            {
                int a = 1;
                Gpu.SyncThreads();
            }
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Lower_ForStatement_WithBreakAndContinue() =>
        // LoopRewriter itself does not flatten nested if-statements, so we should test its specific responsibilities:
        // rewriting loops and updating break/continue inside them.
        VerifyLoopRewrite(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                if (i == 5) continue;
                if (i == 8) break;
                Gpu.SyncThreads();
            }
        });

    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Lower_DoStatement()
    {
        bool condition = true;
        return VerifyLoopRewrite(() =>
        {
            do
            {
                Gpu.SyncThreads();
            } while (condition);
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Lower_ForEachArray_WithBarrier()
    {
        int[] data = [1, 2, 3];
        return VerifyPipelineLower(() =>
        {
            int sum = 0;
            foreach (int x in data)
            {
                sum += x;
                Gpu.SyncThreads();
            }
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_ForStatement_WithNestedBarrierFreeForBreakAndContinue() =>
        VerifyLoopRewrite(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    if (j == 1) continue;
                    if (j == 2) break;
                }

                Gpu.SyncThreads();
            }
        });

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_ForStatement_WithNestedSwitchBreak() =>
        VerifyLoopRewrite(() =>
        {
            int sum = 0;
            for (int i = 0; i < 4; i++)
            {
                switch (i)
                {
                    case 0:
                        sum += 1;
                        break;
                    default:
                        sum += 2;
                        break;
                }

                Gpu.SyncThreads();
            }
        });

    // The continue inside the user's switch stays a goto to the continue label, which stays in front of its state: a
    // goto case would bind to the user's switch (#670)
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_ForStatement_ContinueInSwitch_JumpsToTheLabelOfTheState() =>
        VerifyPipelineLower(() =>
        {
            int sum = 0;
            for (int i = 0; i < 4; i++)
            {
                switch (i)
                {
                    case 0:
                        continue;
                    case 3:
                        sum += 100;
                        break;
                    default:
                        break;
                }

                Gpu.SyncThreads();
                sum += 1;
            }
        });
}
