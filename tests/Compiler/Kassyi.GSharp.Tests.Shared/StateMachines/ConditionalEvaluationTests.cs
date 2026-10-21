using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// A warp primitive whose evaluation is conditional (right operand of a short-circuit operator) or repeated (loop
/// header) must keep that evaluation order after lowering, instead of being hoisted in front of the statement once.
/// </summary>
public class ConditionalEvaluationTests : StateMachineTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_WarpInstruction_OnRightOfAnd()
    {
        bool flag = false;
        int value = 0;
        return VerifyPipelineLower(() =>
        {
            bool ok = flag && Gpu.WarpShuffle(value, 1) > 0;
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_WarpInstruction_OnRightOfOr()
    {
        bool flag = false;
        int value = 0;
        return VerifyPipelineLower(() =>
        {
            bool ok = flag || Gpu.WarpShuffle(value, 1) > 0;
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_WarpInstruction_InNestedShortCircuit()
    {
        bool first = false, second = false;
        int value = 0;
        return VerifyPipelineLower(() =>
        {
            bool ok = first && (second || Gpu.WarpShuffle(value, 1) > 0);
        });
    }

    // The left operand is always evaluated, so plain hoisting is already correct
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_WarpInstruction_OnLeftOfAnd()
    {
        bool flag = false;
        int value = 0;
        return VerifyPipelineLower(() =>
        {
            bool ok = Gpu.WarpShuffle(value, 1) > 0 && flag;
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_WarpInstruction_InWhileCondition()
    {
        int count = 0, limit = 0;
        return VerifyPipelineLower(() =>
        {
            while (Gpu.WarpShuffle(count, 0) < limit)
            {
                count++;
            }
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_WarpInstruction_InForIncrementor()
    {
        int steps = 0, limit = 0;
        return VerifyPipelineLower(() =>
        {
            for (int i = 0; i < limit; i = Gpu.WarpShuffle(i + 1, 0))
            {
                steps++;
            }
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_WarpInstruction_InDoWhileCondition()
    {
        int count = 0, limit = 0;
        return VerifyPipelineLower(() =>
        {
            do
            {
                count++;
            } while (Gpu.WarpShuffle(count, 0) < limit);
        });
    }
}
