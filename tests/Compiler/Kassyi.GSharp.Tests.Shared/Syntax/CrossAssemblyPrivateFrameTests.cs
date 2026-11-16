using Kassyi.GSharp.Tests.DeviceLibrary;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="CrossAssemblyPrivateFrameKernels"/> and <see cref="CrossAssemblyProtectedFrameKernels"/>
/// on the native target (CPU or CUDA): they call suspending device functions of another assembly whose managed frames
/// are private to their declaring classes (#1991). The managed fallback runs the same kernels in
/// <c>CrossAssemblyPrivateFrameManagedTests</c>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
public class CrossAssemblyPrivateFrameTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void KeepAcrossBarrier_LibraryFrameIsPrivate_RunsThroughTheEntries()
    {
        var buffer = new int[4];
        var result = new int[4];

        CrossAssemblyPrivateFrameKernels.LaunchPrivateFrameKernel(buffer, result, blockDimX: 4);

        Assert.Equal([2, 6, 10, 10], result);
        Assert.Equal([1, 2, 3, 4], buffer);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Relay_LibraryFrameIsPrivateThroughCallee_RunsThroughTheEntries()
    {
        var buffer = new int[4];
        var result = new int[4];

        CrossAssemblyPrivateFrameKernels.LaunchPrivateFrameRelayKernel(buffer, result, blockDimX: 4);

        Assert.Equal([5, 13, 21, 21], result);
        Assert.Equal([1, 2, 3, 4], buffer);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AddHundred_ConsumerDeviceFunctionCallsLibraryPrivateFrame_RunsThroughTheEntries()
    {
        var buffer = new int[4];
        var result = new int[4];

        CrossAssemblyPrivateFrameKernels.LaunchPrivateFrameThroughFunctionKernel(buffer, result, blockDimX: 4);

        Assert.Equal([102, 106, 110, 110], result);
        Assert.Equal([1, 2, 3, 4], buffer);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void KeepProtected_DerivedKernelClassCallsProtectedLibraryFunction_RunsThroughTheProtectedEntries()
    {
        var buffer = new int[4];
        var result = new int[4];

        CrossAssemblyProtectedFrameKernels.LaunchProtectedFrameKernel(buffer, result, blockDimX: 4);

        Assert.Equal([2, 6, 10, 10], result);
        Assert.Equal([1, 2, 3, 4], buffer);
    }
}
