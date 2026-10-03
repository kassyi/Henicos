using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class WarpInstructionTests : StateMachineTestBase
{
    [Fact]
    [Trait("Category", "Compiler")]
    public Task Hoist_WarpInstruction_FromAssignment() => VerifyWarpRewrite("""
        {
            int a = x + Gpu.WarpShuffle(y, 0) * z;
        }
        """);

    [Fact]
    [Trait("Category", "Compiler")]
    public Task Hoist_MultipleWarpInstructions_FromSameStatement() => VerifyWarpRewrite("""
        {
            var a = Gpu.WarpShuffle(x, 1) + Gpu.WarpReduceSum(y);
        }
        """);

    [Fact]
    [Trait("Category", "Compiler")]
    public Task NoOp_OnStandaloneWarpInstruction() => VerifyWarpRewrite("""
        {
            Gpu.SyncWarp();
        }
        """);

    [Fact]
    [Trait("Category", "Compiler")]
    public Task Hoist_NestedWarpInstructions() => VerifyWarpRewrite("""
        {
            var a = Gpu.WarpShuffle(Gpu.WarpShuffle(x, 1), 2);
        }
        """);
}
