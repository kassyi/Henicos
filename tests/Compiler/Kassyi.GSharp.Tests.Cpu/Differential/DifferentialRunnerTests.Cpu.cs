using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.DifferentialHarnessFakes;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class DifferentialRunnerTests
{
    partial void RunLaunchPtrDeviceExecutionCase()
    {
        var hostSimCase = new DifferentialCase("HarnessTests.FakePointerKernel")
        {
            Shape = LaunchShapes.SingleThread(),
            Inputs = static (_, _) => new DiffBuffers().RefScalar("r", -99, OutputPolicy.Exact),
            Launch = static (b, _) => b.SetScalar("r", 0),
            LaunchPtr = static (b, _) =>
            {
                using var scope = b.PinOrUpload();
                unsafe
                {
                    int* ptr = (int*)scope.Get("r");
                    ptr[0] = 0;
                }
            },
            Oracle = static (b, s) => HarnessProbeKernels.LaunchLastWriterKernel(
                b.Get<int>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
        };

        var simReport = DifferentialRunner.Evaluate(
            hostSimCase,
            [0],
            [new DiffPathRunner(DiffPath.Cuda, DiffPaths.RunDirect)],
            NoEntries
        );
        Assert.False(simReport.HasFailures, simReport.ToString());
    }
}
