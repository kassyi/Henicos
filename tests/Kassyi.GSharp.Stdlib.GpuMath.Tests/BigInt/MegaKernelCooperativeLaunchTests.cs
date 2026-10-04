using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt.Algorithms;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt.Expressions;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigInt;

[Trait("Category", "GpuExecution")]
public class MegaKernelCooperativeLaunchTests
{
    [SkippableFact]
    public void Test_CooperativeMegaKernel_ClampsToMockedSmCount()
    {
        CudaCapability.EnsureGpuAvailable();

        // Create context with enough memory for a large BigInt
        // We need sizeWords large enough so that (sizeWords / blockDim) > mockedSmCount
        // blockDim in Mega kernels is currently 1024.
        // If sizeWords = 8192, gridDim = 8.
        // We mock SmCount to 2, so it should clamp to 2 and succeed instead of 720 Error.
        int sizeWords = 8192; // 256K bits

        using var context = new GpuWorkspace(sizeWords * 3, GpuExecutionEngine.GSharpAot);

        // Mock the SM count down to 2
        context.DeviceSmCount = 2;

        var arrA = new uint[sizeWords];
        arrA[sizeWords - 1] = 0x7FFFFFFF; // Make it large
        var arrB = new uint[sizeWords];
        arrB[sizeWords - 1] = 0x3FFFFFFF;
        arrB[0] = 1; // avoid zero

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuQ = new GpuBigUInt(context, sizeWords);
        using var gpuR = new GpuBigUInt(context, sizeWords);

        // This invokes SqrtMega / ReciprocalMega implicitly under the hood for large divisions
        // If the gridDim is NOT clamped to context.DeviceSmCount, it might try to launch with 8 blocks.
        // On a device with < 8 SMs, this would throw CUDA_ERROR_LAUNCH_FAILED (720).
        // Even if the device has >= 8 SMs, this proves the kernel can successfully execute with gridDim=2.
        var exception = Record.Exception(() => GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR));

        Assert.Null(exception);
    }
}
