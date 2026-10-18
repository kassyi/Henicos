using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[TestTarget(ExecutionTarget.GpuExecution)]
public class ElseBarrierTests : TranspilerTestBase
{
    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ElseBarrier_ReversesArrayCorrectly()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        int length = 32;
        var input = new int[length];
        var output = new int[length];

        for (int i = 0; i < length; i++)
        {
            input[i] = i;
        }

        ElseBarrierKernels.LaunchReverseWithElseBarrierKernel(
            input,
            output,
            length,
            blockDimX: 32
        );

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(length - 1 - i, output[i]);
        }
    }
}
