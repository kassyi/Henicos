#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class PointerLaunchDifferentialTests
{
    private static partial Action<DiffBuffers, LaunchShape>? GetTwoLengthsLaunchPtr() =>
        static (b, s) =>
        {
            using var scope = b.PinOrUpload();
            PointerLaunchDifferentialKernels.LaunchTwoLengthsKernelPtr(
                scope.GetArray("a"),
                scope.GetArray("b"),
                scope.GetArray("lengths"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
        };

    private static partial Action<DiffBuffers, LaunchShape>? GetOptionalLengthLaunchPtr() =>
        static (b, s) =>
        {
            using var scope = b.PinOrUpload();
            PointerLaunchDifferentialKernels.LaunchOptionalLengthKernelPtr(
                scope.GetArray("optional"),
                scope.GetArray("result"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
        };

    // The checks run before the launch, so they need no GPU
    // A proxy without data that claims elements describes no array
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void LaunchPtr_NoDataWithLength_RejectsProxy()
    {
        var noData = new GSharpArrayProxy { Length = 3 };

        var error = Assert.Throws<ArgumentException>(() =>
            PointerLaunchDifferentialKernels.LaunchTwoLengthsKernelPtr(Proxy(4), noData, Proxy(2)));
        Assert.Equal("b", error.ParamName);
        var optional = Assert.Throws<ArgumentException>(() =>
            PointerLaunchDifferentialKernels.LaunchOptionalLengthKernelPtr(noData, Proxy(1)));
        Assert.Equal("optional", optional.ParamName);
    }

    // An empty array is passed as one, also where the parameter is not nullable
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchPtr_EmptyArray_PassesLengthZero()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var a = new GpuBuffer<int>(LongLength);
        using var lengths = new GpuBuffer<int>(2);
        a.CopyToDevice(new int[LongLength]);

        PointerLaunchDifferentialKernels.LaunchTwoLengthsKernelPtr(
            a, new GSharpArrayProxy(GSharpArrayProxy.EmptyArrayData, 0), lengths.ToProxy(), blockDimX: 32);
        GpuKernelLoader.SynchronizeDevice();

        int[] result = new int[2];
        lengths.CopyToHost(result);
        Assert.Equal([LongLength, 0], result);
    }

    // The typed launcher of an overloaded kernel takes a null buffer for a nullable array and passes null
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchPtr_TypedLauncherNullBuffer_PassesNull()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var result = new GpuBuffer<int>(1);

        PointerLaunchDifferentialKernels.LaunchOverloadedOptionalKernelPtr((GpuBuffer<float>?)null, result);
        GpuKernelLoader.SynchronizeDevice();

        int[] host = new int[1];
        result.CopyToHost(host);
        Assert.Equal([-2], host);
    }

    // A negative length set through the field, which the constructor would reject; an optional array is checked too
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void LaunchPtr_NegativeLength_RejectsNegativeLength()
    {
        var negative = Proxy(4);
        negative.Length = -1;

        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PointerLaunchDifferentialKernels.LaunchTwoLengthsKernelPtr(Proxy(4), Proxy(4), negative));
        Assert.Equal("lengths", error.ParamName);
        var optional = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PointerLaunchDifferentialKernels.LaunchOptionalLengthKernelPtr(negative, Proxy(1)));
        Assert.Equal("optional", optional.ParamName);
    }

    // The overload of the typed launcher is not generated for a non-overloaded kernel; a GpuBuffer converts to a proxy
    // with its length, so the same launcher takes it
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchPtr_GpuBuffers_TakesBuffersWithTheirLengths()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var a = new GpuBuffer<int>(LongLength);
        using var b = new GpuBuffer<int>(ShortLength);
        using var lengths = new GpuBuffer<int>(2);
        a.CopyToDevice(new int[LongLength]);
        b.CopyToDevice(new int[ShortLength]);

        PointerLaunchDifferentialKernels.LaunchTwoLengthsKernelPtr(a, b, lengths.ToProxy(), blockDimX: 32);
        GpuKernelLoader.SynchronizeDevice();

        int[] result = new int[2];
        lengths.CopyToHost(result);
        Assert.Equal([LongLength, ShortLength], result);
    }

    // A proxy with a fake address for the checks, which reject it before anything reads the memory
    private static GSharpArrayProxy Proxy(int length) => new(new IntPtr(0x1000), length);
}
