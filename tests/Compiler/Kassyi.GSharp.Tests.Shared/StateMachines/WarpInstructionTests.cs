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
        uint mask = uint.MaxValue;
        return VerifyWarpRewrite(() =>
        {
            int a = x + Warp.ShuffleSync(mask, y, 0) * z;
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    public Task Hoist_MultipleWarpInstructions_FromSameStatement()
    {
        int x = 0, y = 0;
        uint mask = uint.MaxValue;
        return VerifyWarpRewrite(() =>
        {
            var a = Warp.ShuffleSync(mask, x, 1) + Warp.ShuffleXorSync(mask, y, 1);
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
        uint mask = uint.MaxValue;
        return VerifyWarpRewrite(() =>
        {
            var a = Warp.ShuffleSync(mask, Warp.ShuffleSync(mask, x, 1), 2);
        });
    }

    // The temporary must stay inside the loop body, where the loop variable is in scope and changes per iteration
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Hoist_WarpInstruction_InsideLoopBody()
    {
        int value = 0;
        uint mask = uint.MaxValue;
        return VerifyWarpRewrite(() =>
        {
            for (int offset = 1; offset < 32; offset <<= 1)
            {
                int neighbor = Warp.ShuffleSync(mask, value, offset) + 1;
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
        uint mask = uint.MaxValue;
        return VerifyWarpRewrite(() =>
        {
            if (lane > 0)
            {
                value = Warp.ShuffleSync(mask, value, lane) * 2;
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
        uint mask = uint.MaxValue;
        return VerifyWarpRewrite(() =>
        {
            for (int offset = 1; offset < 32; offset <<= 1)
                value += Warp.ShuffleSync(mask, value, offset);
        });
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Hoist_WarpInstruction_InsideSwitchSection()
    {
        int value = 0, mode = 0;
        uint mask = uint.MaxValue;
        return VerifyWarpRewrite(() =>
        {
            switch (mode)
            {
                case 0:
                    value = Warp.ShuffleSync(mask, value, 1) + 1;
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
        uint mask = uint.MaxValue;
        return VerifyWarpRewrite(() =>
        {
            for (int outer = 0; outer < 2; outer++)
            {
                for (int offset = 1; offset < 32; offset <<= 1)
                {
                    value += Warp.ShuffleSync(mask, value, offset + outer);
                }
            }
        });
    }
}
