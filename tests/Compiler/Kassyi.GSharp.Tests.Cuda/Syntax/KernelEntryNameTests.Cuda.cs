using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Syntax.GlobalNames;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class KernelEntryNameTests
{
    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_OverloadedKernelsWithTypedPtrWrappers_RunsSelectedOverload()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var intBuffer = new GpuBuffer<int>(ThreadCount);
        using var floatBuffer = new GpuBuffer<float>(ThreadCount);

        OverloadedKernels.LaunchFillKernelPtr(intBuffer, blockDimX: ThreadCount);
        OverloadedKernels.LaunchFillKernelPtr(floatBuffer, blockDimX: ThreadCount);

        var ints = new int[ThreadCount];
        var floats = new float[ThreadCount];
        intBuffer.CopyToHost(ints);
        floatBuffer.CopyToHost(floats);
        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(i * 2, ints[i]);
            Assert.Equal(i * 0.5f, floats[i]);
        }
    }
}
