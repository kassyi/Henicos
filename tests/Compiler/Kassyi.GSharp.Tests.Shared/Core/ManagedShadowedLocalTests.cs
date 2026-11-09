using System;
using Xunit;
#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
#endif

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The kernels of <see cref="ManagedShadowedLocalKernels"/> on the native target (CPU or CUDA) and, on the CPU, on the
/// managed fallback, which copies each macro lambda body into the method: both must give the values C# gives.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public class ManagedShadowedLocalTests : TranspilerTestBase
{
    private const int Count = 4;

    // 2i from the lambda's v; result[0] also gets the method's v (1)
    private static readonly int[] s_gridStrideExpected = [1, 2, 4, 6];

    // result[0] from the lambda's v (2), result[1] from the method's v (1)
    private static readonly int[] s_singleThreadExpected = [2, 1, 0, 0];

    // k from the lambda's v, then the method's v (1)
    private static readonly int[] s_forUnrollExpected = [0, 1, 2, 3, 1];

    // i + 10 + 100i + 1000; result[0] also gets the later local (3)
    private static readonly int[] s_nestedAndLaterExpected = [1013, 1111, 1212, 1313];

    // 2i (7 for i = 0, from Gpu.SingleThread) + the method's v (3)
    private static readonly int[] s_managedVersionExpected = [10, 5, 7, 9];

    public static TheoryData<string> Kernels => ["GridStrideLoop", "SingleThread", "ForUnroll", "NestedAndLater",
        "ManagedVersion"];

    [Theory]
    [MemberData(nameof(Kernels))]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LambdaLocalShadowingMethodLocal(string kernel)
    {
        var (result, expected) = Launch(kernel);

        Assert.Equal(expected, result);
    }

#if GSHARP_CPU
    [Theory]
    [MemberData(nameof(Kernels))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_LambdaLocalShadowingMethodLocal(string kernel)
    {
        int[] result = [];
        int[] expected = [];

        ManagedKernelRun.Run(() => (result, expected) = Launch(kernel));

        Assert.Equal(expected, result);
    }
#endif

    // One block of Count threads, so that a grid-stride loop gives each thread one index
    private static (int[] Result, int[] Expected) Launch(string kernel)
    {
        switch (kernel)
        {
            case "GridStrideLoop":
            {
                var result = new int[Count];
                ManagedShadowedLocalKernels.LaunchGridStrideLoopKernel(result, Count, blockDimX: Count);
                return (result, s_gridStrideExpected);
            }
            case "SingleThread":
            {
                var result = new int[Count];
                ManagedShadowedLocalKernels.LaunchSingleThreadKernel(result, blockDimX: Count);
                return (result, s_singleThreadExpected);
            }
            case "ForUnroll":
            {
                var result = new int[Count + 1];
                ManagedShadowedLocalKernels.LaunchForUnrollKernel(result, blockDimX: 1);
                return (result, s_forUnrollExpected);
            }
            case "NestedAndLater":
            {
                var result = new int[Count];
                ManagedShadowedLocalKernels.LaunchNestedAndLaterKernel(result, Count, blockDimX: Count);
                return (result, s_nestedAndLaterExpected);
            }
            case "ManagedVersion":
            {
                var result = new int[Count];
                ManagedShadowedLocalKernels.LaunchManagedVersionKernel(result, Count, blockDimX: Count);
                return (result, s_managedVersionExpected);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Unknown kernel.");
        }
    }
}
