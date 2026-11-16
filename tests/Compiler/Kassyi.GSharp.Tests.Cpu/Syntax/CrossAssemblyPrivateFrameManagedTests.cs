using Kassyi.GSharp.Tests.DeviceLibrary;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The kernels of <see cref="CrossAssemblyPrivateFrameKernels"/> and <see cref="CrossAssemblyProtectedFrameKernels"/>
/// on the managed fallback, which holds the library's private frames as <see cref="object"/> and runs them through the
/// library's generated entries (#1991). The values are those of <see cref="CrossAssemblyPrivateFrameTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.GlobalStateSequential)]
public class CrossAssemblyPrivateFrameManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void KeepAcrossBarrier_LibraryFrameIsPrivate_RunsThroughTheEntries()
    {
        var buffer = new int[4];
        var result = new int[4];

        ManagedKernelRun.Run(() =>
            CrossAssemblyPrivateFrameKernels.LaunchPrivateFrameKernel(buffer, result, blockDimX: 4));

        Assert.Equal([2, 6, 10, 10], result);
        Assert.Equal([1, 2, 3, 4], buffer);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Relay_LibraryFrameIsPrivateThroughCallee_RunsThroughTheEntries()
    {
        var buffer = new int[4];
        var result = new int[4];

        ManagedKernelRun.Run(() =>
            CrossAssemblyPrivateFrameKernels.LaunchPrivateFrameRelayKernel(buffer, result, blockDimX: 4));

        Assert.Equal([5, 13, 21, 21], result);
        Assert.Equal([1, 2, 3, 4], buffer);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void AddHundred_ConsumerDeviceFunctionCallsLibraryPrivateFrame_RunsThroughTheEntries()
    {
        var buffer = new int[4];
        var result = new int[4];

        ManagedKernelRun.Run(() =>
            CrossAssemblyPrivateFrameKernels.LaunchPrivateFrameThroughFunctionKernel(buffer, result, blockDimX: 4));

        Assert.Equal([102, 106, 110, 110], result);
        Assert.Equal([1, 2, 3, 4], buffer);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void KeepProtected_DerivedKernelClassCallsProtectedLibraryFunction_RunsThroughTheProtectedEntries()
    {
        var buffer = new int[4];
        var result = new int[4];

        ManagedKernelRun.Run(() =>
            CrossAssemblyProtectedFrameKernels.LaunchProtectedFrameKernel(buffer, result, blockDimX: 4));

        Assert.Equal([2, 6, 10, 10], result);
        Assert.Equal([1, 2, 3, 4], buffer);
    }
}
