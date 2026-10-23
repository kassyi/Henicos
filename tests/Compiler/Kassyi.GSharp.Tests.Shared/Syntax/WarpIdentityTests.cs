using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class WarpIdentityTests
{
    // The CPU dialect lowers a kernel with a barrier into a state machine, which is where Gpu.WarpId() was taken for a
    // warp shuffle (GSH0048). The kernel lives in TestCases: __warp_id / __lane_id have no native definition yet.
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public async Task WarpIdAndLaneId_AroundBarrier_DoNotSplitTheStateMachine()
    {
        await RoslynTestUtility.TestGenerator("Syntax/WarpIdentityBarrierKernels.cu.cs")
                               .WithDialect("Cpu")
                               .HasNoErrorsAsync();
    }
}
