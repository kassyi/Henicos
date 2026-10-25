using System.Numerics;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[TestTarget(ExecutionTarget.GpuExecution)]
public class CopyAsyncTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CopyAsync_Int4Bytes_CopiesAndReversesViaSharedMemory()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var input = new int[32];
        var output = new int[32];
        for (int i = 0; i < 32; i++)
        {
            input[i] = 100 + i;
        }

        CopyAsyncKernels.LaunchCopyAsyncIntKernel(input, output, blockDimX: 32);

        for (int i = 0; i < 32; i++)
        {
            Assert.Equal(131 - i, output[i]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CopyAsync_Vector4_16Bytes_CopiesAndReadsNeighborViaSharedMemory()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var input = new Vector4[32];
        var output = new Vector4[32];
        for (int i = 0; i < 32; i++)
        {
            input[i] = new Vector4(i, 2 * i, 3 * i, 4 * i);
        }

        CopyAsyncKernels.LaunchCopyAsyncVector4Kernel(input, output, blockDimX: 32);

        for (int i = 0; i < 32; i++)
        {
            Assert.Equal(input[(i + 1) % 32], output[i]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CopyAsync_TwoCommitGroups_WaitGroup1ThenWaitAll_CompletesEachHalf()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var input = new int[32];
        var output = new int[32];
        for (int i = 0; i < 32; i++)
        {
            input[i] = 200 + i;
        }

        CopyAsyncKernels.LaunchCopyAsyncTwoGroupsKernel(input, output, blockDimX: 32);

        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(input[15 - i], output[i]);
        }

        for (int i = 16; i < 32; i++)
        {
            Assert.Equal(input[47 - i], output[i]);
        }
    }
}
