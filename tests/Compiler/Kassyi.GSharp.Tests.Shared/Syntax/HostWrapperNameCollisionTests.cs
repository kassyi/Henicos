using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Syntax.HostWrapperCollision;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class HostWrapperNameCollisionTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteCollisionKernel_WithHostWrapperLocalAndTypeCollisions_ProducesExpectedResults()
    {
        var args = new[] { 10, 20, 30, 40 };
        var payload = new HostWrapperNameCollisionKernels.CollisionPayload
        {
            Data = [1, 2, 3, 4],
            Scale = 5
        };
        int pinned = 7;

        var returned = HostWrapperNameCollisionKernels.LaunchExecuteCollisionKernel(
            hModule: 1,
            hFunc: 2,
            args: args,
            totalThreads: 3,
            dllPath: 4,
            hLib: 5,
            fnPtr: 6,
            invoke: 8,
            totalBlocks: 9,
            blockSize: 11,
            startBlock: 12,
            endBlock: 13,
            native: 14,
            host: 15,
            pinned: ref pinned,
            payload: payload,
            gridDimX: 1,
            blockDimX: 4);

        // Sum of scalar parameters (1+2+3+4+5+6+8+9+11+12+13+14+15) + initial pinned (7) = 110
        Assert.Equal(17, pinned);
        Assert.Equal([115, 120, 125, 130], args);
        Assert.Equal([116, 121, 126, 131], returned);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteCollisionKernel_CpuManagedFallback_ProducesExpectedResults()
    {
        Skip.IfNot(IsCpuDialect, "CPU managed fallback test applies only to the CPU dialect");

        var args = new[] { 10, 20, 30, 40 };
        var payload = new HostWrapperNameCollisionKernels.CollisionPayload
        {
            Data = [1, 2, 3, 4],
            Scale = 5
        };
        int pinned = 7;

        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            var returned = HostWrapperNameCollisionKernels.LaunchExecuteCollisionKernel(
                hModule: 1,
                hFunc: 2,
                args: args,
                totalThreads: 3,
                dllPath: 4,
                hLib: 5,
                fnPtr: 6,
                invoke: 8,
                totalBlocks: 9,
                blockSize: 11,
                startBlock: 12,
                endBlock: 13,
                native: 14,
                host: 15,
                pinned: ref pinned,
                payload: payload,
                gridDimX: 1,
                blockDimX: 4);

            Assert.Equal(17, pinned);
            Assert.Equal([115, 120, 125, 130], args);
            Assert.Equal([116, 121, 126, 131], returned);
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
    }
}
