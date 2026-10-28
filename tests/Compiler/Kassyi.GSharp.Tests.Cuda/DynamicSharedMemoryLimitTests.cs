using Kassyi.GSharp.Runtime.Interop;
using System.Text;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Reproduction and measurement of the CUDA launch limits (#545): dynamic shared memory above 48 KB needs
/// <c>cuFuncSetAttribute(MAX_DYNAMIC_SHARED_SIZE_BYTES)</c>, which G# never calls.
/// The reproduction asserts the CURRENT (wrong) behavior; when #545 is fixed it must be inverted.
/// </summary>
[Collection(TestCollections.DeviceExclusive)]
public class DynamicSharedMemoryLimitTests : TranspilerTestBase
{
    private const int BlockSize = 256;

    private static uint Launch(uint smemBytes, uint gridDimX = 1, uint blockDimX = BlockSize, uint blockDimZ = 1)
    {
        var output = new int[BlockSize];
        DynamicSharedMemoryLimitKernels.LaunchBigSmemKernel(output, (int)(smemBytes / 4), gridDimX: gridDimX,
            blockDimX: blockDimX, blockDimZ: blockDimZ, sharedMemBytes: smemBytes);
        return smemBytes;
    }

    private static string Try(Action action)
    {
        try
        {
            action();
            return "OK";
        }
        catch (CudaException ex)
        {
            return $"CudaException {ex.Result} | {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name} | {ex.Message}";
        }
    }

    private static int Attribute(int device, int attribute)
    {
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out int value,
            (CudaDriverApi.CuDeviceAttribute)attribute, device));
        return value;
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_DynamicSharedMemory64KB_FailsToLaunchBecauseOptInIsMissing()
    {
        // Current wrong behavior (#545): 64 KB is within the 99 KB opt-in limit of sm_86 but the launch is rejected.
        // When the opt-in is implemented this test must expect success and check the reversed data instead.
        var ex = Assert.Throws<CudaException>(() => Launch(64 * 1024));
        Assert.Equal(CUresult.CudaErrorInvalidValue, ex.Result);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DynamicSharedMemory48KB_Launches()
    {
        var output = new int[BlockSize];
        uint bytes = 48 * 1024;
        DynamicSharedMemoryLimitKernels.LaunchBigSmemKernel(output, (int)(bytes / 4), blockDimX: BlockSize,
            sharedMemBytes: bytes);
        Assert.Equal((int)(bytes / 4) - 1, output[0]);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LaunchLimits_AreMeasuredAndWritten()
    {
        CudaException.ThrowIfError(CudaDriverApi.cuInit(0));
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out int device, 0));
        var sb = new StringBuilder();

        foreach (var (name, id) in new[]
        {
            ("MAX_SHARED_MEMORY_PER_BLOCK", 8),
            ("MAX_SHARED_MEMORY_PER_BLOCK_OPTIN", 97),
            ("MULTIPROCESSOR_COUNT", 16),
            ("MAX_SHARED_MEMORY_PER_MULTIPROCESSOR", 81),
            ("RESERVED_SHARED_MEMORY_PER_BLOCK", 111),
            ("MAX_THREADS_PER_BLOCK", 1),
            ("MAX_BLOCK_DIM_Z", 4),
            ("MAX_GRID_DIM_X", 5)
        })
        {
            string text;
            try
            {
                text = Attribute(device, id).ToString();
            }
            catch (CudaException ex)
            {
                text = $"CudaException {ex.Result}";
            }

            sb.AppendLine($"attr {name} = {text}");
        }

        foreach (uint bytes in new uint[] { 48 * 1024, 48 * 1024 + 4, 64 * 1024, 96 * 1024, 99 * 1024, 100 * 1024 })
        {
            sb.AppendLine($"smem {bytes} B: {Try(() => Launch(bytes))}");
        }

        // sharedMemBytes is not a multiple of 4 (n * 4 < bytes); 48 KB + 1 byte
        sb.AppendLine($"smem 49153 B (48KB+1): {Try(() => DynamicSharedMemoryLimitKernels.LaunchBigSmemKernel(new int[BlockSize], 12288, blockDimX: BlockSize, sharedMemBytes: 49153))}");
        sb.AppendLine($"gridDimX=0: {Try(() => Launch(1024, gridDimX: 0))}");
        sb.AppendLine($"blockDimX=0: {Try(() => Launch(1024, blockDimX: 0))}");
        sb.AppendLine($"blockDimX=1025: {Try(() => Launch(1024, blockDimX: 1025))}");
        sb.AppendLine($"blockDimZ=65 (blockDimX=1): {Try(() => Launch(1024, blockDimX: 1, blockDimZ: 65))}");

        string path = RepositoryPaths.Combine("tmp", "device-limits-545.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); // path always has a parent under the repository root
        File.WriteAllText(path, sb.ToString());
        Assert.NotEmpty(sb.ToString());
    }
}
