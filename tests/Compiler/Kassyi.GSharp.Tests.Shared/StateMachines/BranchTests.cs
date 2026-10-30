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

    // A barrier in a switch section (#708): the switch becomes a dispatch block that evaluates the governing expression
    // once and compares it with each case label (two on one section), labeled sections and an end label; a value that
    // matches no label goes to the default section
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_Switch_DispatchesEachLabelAndFallsBackToDefault()
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
                    break;
                default:
                    value = 2;
                    break;
            }
        });
    }

    // break (also inside an if that holds a barrier), goto default and goto case become gotos to the end and section
    // labels. This test has no semantic model, so it also covers the fallback that matches goto case Last to case Last
    // by equivalent expressions
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_Switch_BreakAndGotoBecomeGotos_MatchedByExpressionWithoutModel()
    {
        int flag = 0;
        int value = 0;
        return VerifyBranchRewrite(() =>
        {
            switch (flag)
            {
                case 0:
                    Gpu.SyncThreads();
                    goto default;
                case 1:
                    goto case Last;
                case Last:
                    if (value > 1)
                    {
                        Gpu.SyncThreads();
                        break;
                    }

                    value += 2;
                    break;
                default:
                    value = 3;
                    break;
            }
        });
    }

    // A break of a loop nested in a section, and a break and goto case of a switch nested in it, stay the jumps of the
    // nested statement
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_Switch_NestedLoopAndSwitchKeepTheirJumps()
    {
        int flag = 0;
        int value = 0;
        return VerifyBranchRewrite(() =>
        {
            switch (flag)
            {
                case 0:
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

                    Gpu.SyncThreads();
                    break;
                default:
                    value = 1;
                    break;
            }
        });
    }

    // goto case 66 in a char switch reaches case 'B' (#708): the flattened goto is matched by constant value with the
    // semantic model, across the int and char types of the two constants
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Pipeline_SwitchGotoCaseChar_MatchesCaseByValue() =>
        VerifyPipelineLowerFromTestCase("SwitchGotoCaseChar.cs");

    // goto case (Mode)2 in an enum switch reaches case Mode.Third (#708)
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Pipeline_SwitchGotoCaseEnum_MatchesCaseByValue() =>
        VerifyPipelineLowerFromTestCase("SwitchGotoCaseEnum.cs");

    // goto case 1 in a float switch reaches case 1f (#894 review): the keys follow the governing type, so the int constant
    // of the goto and the float constant of the label compare equal
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Pipeline_SwitchGotoCaseFloat_MatchesCaseByGoverningType() =>
        VerifyPipelineLowerFromTestCase("SwitchGotoCaseFloat.cs");

    // goto case 1.0 and goto case 1.5 in an int switch reach case 1, as C# binds them (#894 review): the gotos are
    // matched to the label symbol that the semantic model binds, not by a converted value
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Pipeline_SwitchGotoCaseIntFromDouble_ReachesConvertedCase() =>
        VerifyPipelineLowerFromTestCase("SwitchGotoCaseIntFromDouble.cs");

    // goto case 0.1 in a float switch reaches case 0.1f (#894 review): C# rounds the double to float first
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Pipeline_SwitchGotoCaseFloatRounded_ReachesRoundedCase() =>
        VerifyPipelineLowerFromTestCase("SwitchGotoCaseFloatRounded.cs");

    // case float.NaN of a flattened switch is tested with temp != temp (#894 review), which matches NaN as C# does
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Pipeline_SwitchNaNCase_DispatchTestsNaN() =>
        VerifyPipelineLowerFromTestCase("SwitchNaNCase.cs");

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
