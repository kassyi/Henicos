#nullable enable
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Syntax.HostWrapperCollision;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class HostWrapperNameCollisionTests
{
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteCollisionKernel_CpuManagedFallback_ComputesWithoutNameClashes()
    {
        var args = new[] { 10, 20, 30, 40 };
        var payload = new HostWrapperNameCollisionKernels.CollisionPayload
        {
            Data = [1, 2, 3, 4],
            Scale = 5
        };
        int pinned = 7;

        using (GpuExecution.Use(KernelExecution.Managed))
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
                kernelParams: new VerifyPair { A = 7, B = 11 },
                kernelParamsValue: new VerifyPair { A = 10, B = 20 },
                gridDimX: 1,
                blockDimX: 4);

            Assert.Equal(17, pinned);
            Assert.Equal([163, 168, 173, 178], args);
            Assert.Equal([164, 169, 174, 179], returned);
        }
    }
}
