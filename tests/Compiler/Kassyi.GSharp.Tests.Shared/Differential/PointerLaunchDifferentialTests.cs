#nullable enable
using System;
#if GSHARP_CUDA
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
#endif
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// <c>Launch{Kernel}Ptr</c> takes each array as a <c>GSharpArrayProxy</c>: the kernel sees the length the proxy carries,
/// as the array launcher passes the array's length, and the launcher checks the proxies as it checks arrays (#720).
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class PointerLaunchDifferentialTests
{
    private const int LongLength = 8;
    private const int ShortLength = 5;

    internal static readonly DifferentialCase s_twoLengths = new(
        $"{nameof(PointerLaunchDifferentialKernels)}.{nameof(PointerLaunchDifferentialKernels.TwoLengthsKernel)}")
    {
        Shape = LaunchShapes.Linear(LongLength, blockX: 32),
        Seeds = [0, 1],
        Inputs = static (rng, _) =>
        {
            // Seed 1 swaps the lengths, so each argument is the longer one once
            var (aLength, bLength) = rng.Seed == 0 ? (LongLength, ShortLength) : (ShortLength, LongLength);
            int[] a = new int[aLength];
            int[] b = new int[bLength];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = i + 1;
            }

            for (int i = 0; i < b.Length; i++)
            {
                b[i] = 100 * (i + 1);
            }

            return new DiffBuffers()
                .In("a", a, OutputPolicy.Exact)
                .In("b", b, OutputPolicy.Exact)
                .Out<int>("lengths", 2, OutputPolicy.Exact);
        },
        Oracle = static (b, _) => PointerLaunchDifferentialKernels.TwoLengthsKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<int>("lengths")
        ),
        Launch = static (b, s) => PointerLaunchDifferentialKernels.LaunchTwoLengthsKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<int>("lengths"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
#if GSHARP_CUDA
        LaunchPtr = static (b, s) =>
        {
            using var scope = b.PinOrUpload();
            PointerLaunchDifferentialKernels.LaunchTwoLengthsKernelPtr(
                scope.GetArray("a"),
                scope.GetArray("b"),
                scope.GetArray("lengths"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
        },
#endif
    };

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchPtr_MultipleArrays_EachArraySeesItsOwnLength() => DifferentialRunner.Run(s_twoLengths);

    internal static readonly DifferentialCase s_optionalLength = new(
        $"{nameof(PointerLaunchDifferentialKernels)}.{nameof(PointerLaunchDifferentialKernels.OptionalLengthKernel)}")
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0, 1, 2],
        Inputs = static (rng, _) => new DiffBuffers()
            .InNullable("optional", rng.Seed switch
            {
                0 => null,
                1 => Array.Empty<int>(),
                _ => new[] { 4, 5, 6 }
            })
            .Out<int>("result", 1, OutputPolicy.Exact),
        Oracle = static (b, _) => PointerLaunchDifferentialKernels.OptionalLengthKernel(
            b.GetNullable<int>("optional"),
            b.Get<int>("result")
        ),
        Launch = static (b, s) => PointerLaunchDifferentialKernels.LaunchOptionalLengthKernel(
            b.GetNullable<int>("optional"),
            b.Get<int>("result"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
#if GSHARP_CUDA
        LaunchPtr = static (b, s) =>
        {
            using var scope = b.PinOrUpload();
            PointerLaunchDifferentialKernels.LaunchOptionalLengthKernelPtr(
                scope.GetArray("optional"),
                scope.GetArray("result"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
        },
#endif
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchPtr_OptionalArrays_NullAndEmptyArraysStayApart() => DifferentialRunner.Run(s_optionalLength);

#if GSHARP_CUDA
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
#endif
}
