using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.DifferentialHarnessFakes;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class DifferentialRunnerTests
{
    partial void RunLaunchPtrDeviceExecutionCase()
    {
        CudaCapability.EnsureGpuAvailable();
        var cudaPtrCase = new DifferentialCase("HarnessProbeKernels.LastWriterKernel")
        {
            Shape = LaunchShapes.SingleThread(),
            Inputs = static (_, _) => new DiffBuffers().RefScalar("r", -99, OutputPolicy.Exact),
            Launch = static (b, s) => HarnessProbeKernels.LaunchLastWriterKernel(
                b.Get<int>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            LaunchPtr = static (b, s) =>
            {
                using var scope = b.PinOrUpload();
                HarnessProbeKernels.LaunchLastWriterKernelPtr(
                    scope.GetArray("r"),
                    gridDimX: s.GridX,
                    blockDimX: s.BlockX
                );
            },
            OracleIsLaunch = true,
        };

        var cudaReport = DifferentialRunner.Evaluate(cudaPtrCase, [0], DiffPaths.Available, NoEntries);
        Assert.False(cudaReport.HasFailures, cudaReport.ToString());
    }
}
