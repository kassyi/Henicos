namespace Kassyi.GSharp.Tests.Shared.Marshaling;

public class ProtectedNestedLaunchTests
{
    private sealed class DerivedCaller : ProtectedNestedLaunchBase
    {
        public static void RunScale(int[] values, int factor)
        {
            var payload = new ProtectedPayload { Values = values, Factor = factor };
            Kernels.LaunchScaleKernel(payload, blockDimX: (uint)values.Length);
        }

        public static void RunDirectScale(int[] values, int factor)
        {
            var payload = new ProtectedPayload { Values = values, Factor = factor };
            LaunchDirectScaleKernel(payload, blockDimX: (uint)values.Length);
        }
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void ProtectedNestedKernel_DerivedCaller_CompilesAndRuns()
    {
        int[] values = [2, 4, 6];

        DerivedCaller.RunScale(values, 3);
        Assert.Equal([6, 12, 18], values);

        DerivedCaller.RunDirectScale(values, 5);
        Assert.Equal([11, 17, 23], values);
    }
}
