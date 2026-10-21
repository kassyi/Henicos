using System.Threading.Tasks;
using Xunit;


namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class WarpInstructionTests : StateMachineTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Hoist_WarpInstruction_FromAssignment()
    {
        int x = 0, y = 0, z = 0;
        return VerifyWarpRewrite(() =>
        {
            int a = x + Gpu.WarpShuffle(y, 0) * z;
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Hoist_MultipleWarpInstructions_FromSameStatement()
    {
        int x = 0, y = 0;
        return VerifyWarpRewrite(() =>
        {
            var a = Gpu.WarpShuffle(x, 1) + Gpu.WarpReduceSum(y);
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public Task NoOp_OnStandaloneWarpInstruction() => VerifyWarpRewrite(() =>
    {
        Gpu.SyncWarp();
    });

    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Hoist_NestedWarpInstructions()
    {
        int x = 0;
        return VerifyWarpRewrite(() =>
        {
            var a = Gpu.WarpShuffle(Gpu.WarpShuffle(x, 1), 2);
        });
    }

    // The temporary must stay inside the loop body, where the loop variable is in scope and changes per iteration
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Hoist_WarpInstruction_InsideLoopBody()
    {
        int value = 0;
        return VerifyWarpRewrite(() =>
        {
            for (int offset = 1; offset < 32; offset <<= 1)
            {
                int neighbor = Gpu.WarpShuffle(value, offset) + 1;
                value += neighbor;
            }
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Hoist_WarpInstruction_InsideIfBlock()
    {
        int value = 0, lane = 0;
        return VerifyWarpRewrite(() =>
        {
            if (lane > 0)
            {
                value = Gpu.WarpShuffle(value, lane) * 2;
            }
        });
    }

    // A non-block embedded statement has no list to receive the temporary, so it must become a block
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Hoist_WarpInstruction_InsideEmbeddedStatement()
    {
        int value = 0;
        return VerifyWarpRewrite(() =>
        {
            for (int offset = 1; offset < 32; offset <<= 1)
                value += Gpu.WarpShuffle(value, offset);
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Hoist_WarpInstruction_InsideSwitchSection()
    {
        int value = 0, mode = 0;
        return VerifyWarpRewrite(() =>
        {
            switch (mode)
            {
                case 0:
                    value = Gpu.WarpShuffle(value, 1) + 1;
                    break;
            }
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Hoist_WarpInstruction_InsideNestedLoops()
    {
        int value = 0;
        return VerifyWarpRewrite(() =>
        {
            for (int outer = 0; outer < 2; outer++)
            {
                for (int offset = 1; offset < 32; offset <<= 1)
                {
                    value += Gpu.WarpShuffle(value, offset + outer);
                }
            }
        });
    }
}
