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
}
