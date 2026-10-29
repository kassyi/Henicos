#pragma warning disable CS0219
using System.Threading.Tasks;
using Xunit;


namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class BranchTests : StateMachineTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Lower_IfStatement_WithoutElse()
    {
        bool condition = true;
        return VerifyBranchRewrite(() =>
        {
            if (condition)
            {
                int a = 1;
                Gpu.SyncThreads();
            }
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Lower_IfStatement_WithElse()
    {
        bool condition = true;
        return VerifyBranchRewrite(() =>
        {
            if (condition)
            {
                int a = 1;
            }
            else
            {
                int b = 2;
                Gpu.SyncThreads();
            }
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_IfStatement_WithNonBlockElseBarrier()
    {
        bool condition = true;
        return VerifyBranchRewrite(() =>
        {
            if (condition)
            {
                int a = 1;
            }
            else Gpu.SyncThreads();
        });
    }

    private const int Last = 2;

    // A barrier in a switch section (#708): the switch becomes a dispatch block, labeled sections and an end label;
    // break, goto case (matched by its expression without a semantic model) and goto default become gotos, a break of a
    // nested loop or switch and a goto case of a nested switch stay theirs
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_SwitchStatement_WithBarrierInSection()
    {
        int flag = 0;
        int value = 0;
        return VerifyBranchRewrite(() =>
        {
            switch (flag + 1)
            {
                case 0:
                case Last:
                    value = 1;
                    Gpu.SyncThreads();
                    goto default;
                case 1:
                    for (int i = 0; i < 2; i++)
                    {
                        if (i == 1)
                        {
                            break;
                        }
                    }

                    switch (value)
                    {
                        case 3:
                            break;
                        default:
                            goto case 3;
                    }

                    goto case Last;
                default:
                    if (value > 1)
                    {
                        Gpu.SyncThreads();
                        break;
                    }

                    value += 2;
                    break;
            }
        });
    }

    // A switch without a split point stays a switch, and one without a default jumps to its end when nothing matches
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_SwitchStatement_OnlyTheSwitchWithBarrierIsFlattened()
    {
        int flag = 0;
        int value = 0;
        return VerifyBranchRewrite(() =>
        {
            switch (flag)
            {
                case 0:
                    value = 1;
                    break;
            }

            switch (flag)
            {
                case 1:
                    Gpu.SyncThreads();
                    break;
            }
        });
    }
}
