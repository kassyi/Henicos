namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class UsingStaticMathKernelTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void UsingStaticMath_MatchesCSharp()
    {
        double[] inputs = [-4.0, -0.25, 0.0, 2.0, 9.0, 100.0];
        int[] intInputs = [-9, -5, 0, 3, 5, 12];
        int n = inputs.Length;
        double[] results = new double[n];
        int[] intResults = new int[n];

        UsingStaticMathKernels.LaunchUsingStaticMathKernel(inputs, results, intInputs, intResults, blockDimX: (uint)n);

        for (int i = 0; i < n; i++)
        {
            double expected = Math.Sqrt(Math.Abs(inputs[i])) + Math.Max(inputs[i], 1.0) + Math.Min(inputs[i], -1.0) + Math.PI;
            Assert.Equal(expected, results[i], 12);
            Assert.Equal(Math.Clamp(intInputs[i], -5, 5), intResults[i]);
        }
    }
}
