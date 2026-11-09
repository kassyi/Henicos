using System;
using Xunit;
#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
#endif

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// One run of a kernel of <see cref="ManagedShadowedLocalKernels"/>: how to launch it (one block of
/// <see cref="ShadowCase.Count"/> threads unless the case says otherwise), and the C# result it must give.
/// </summary>
/// <param name="Launch">Launches the kernel and returns its result array.</param>
/// <param name="Expected">The values the result array must hold.</param>
internal sealed record ShadowCase(Func<int[]> Launch, int[] Expected)
{
    /// <summary>The threads of the block, so that a grid-stride loop gives each thread one index.</summary>
    public const int Count = 4;

    // 2i from the lambda's v; result[0] also gets the method's v (1)
    public static readonly ShadowCase GridStrideLoop = new(
        static () => Run(Count, static result =>
            ManagedShadowedLocalKernels.LaunchGridStrideLoopKernel(result, Count, blockDimX: Count)),
        [1, 2, 4, 6]);

    // result[0] from the lambda's v (2), result[1] from the method's v (1)
    public static readonly ShadowCase SingleThread = new(
        static () => Run(Count, static result =>
            ManagedShadowedLocalKernels.LaunchSingleThreadKernel(result, blockDimX: Count)),
        [2, 1, 0, 0]);

    // k from the lambda's v, then the method's v (1); one thread
    public static readonly ShadowCase ForUnroll = new(
        static () => Run(Count + 1, static result =>
            ManagedShadowedLocalKernels.LaunchForUnrollKernel(result, blockDimX: 1)),
        [0, 1, 2, 3, 1]);

    // i + 10 + 100i + 1000; result[0] also gets the later local (3)
    public static readonly ShadowCase NestedAndLater = new(
        static () => Run(Count, static result =>
            ManagedShadowedLocalKernels.LaunchNestedAndLaterKernel(result, Count, blockDimX: Count)),
        [1013, 1111, 1212, 1313]);

    // 3i + 10; result[0] also gets 1 + 100
    public static readonly ShadowCase LocalFunction = new(
        static () => Run(Count, static result =>
            ManagedShadowedLocalKernels.LaunchLocalFunctionKernel(result, Count, blockDimX: Count)),
        [111, 13, 16, 19]);

    // 2i (7 for i = 0, from Gpu.SingleThread) + the method's v (3)
    public static readonly ShadowCase ManagedVersion = new(
        static () => Run(Count, static result =>
            ManagedShadowedLocalKernels.LaunchManagedVersionKernel(result, Count, blockDimX: Count)),
        [10, 5, 7, 9]);

    private static int[] Run(int length, Action<int[]> launch)
    {
        var result = new int[length];
        launch(result);
        return result;
    }
}

/// <summary>
/// The kernels of <see cref="ManagedShadowedLocalKernels"/> on the native target (CPU or CUDA) and, on the CPU, on the
/// managed fallback, which copies each macro lambda body into the method: both must give the values C# gives.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public class ManagedShadowedLocalTests : TranspilerTestBase
{
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_GridStrideLoop_LambdaLocalShadowsMethodLocal() => RunNative(ShadowCase.GridStrideLoop);

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SingleThread_LambdaLocalShadowsMethodLocal() => RunNative(ShadowCase.SingleThread);

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ForUnroll_LambdaLocalShadowsMethodLocal() => RunNative(ShadowCase.ForUnroll);

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_NestedAndLater_LambdaLocalsShadowOuterNames() => RunNative(ShadowCase.NestedAndLater);

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LocalFunction_ShadowsAndIsShadowed() => RunNative(ShadowCase.LocalFunction);

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ManagedVersion_LambdaLocalShadowsMethodLocal() => RunNative(ShadowCase.ManagedVersion);

#if GSHARP_CPU
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_GridStrideLoop_LambdaLocalShadowsMethodLocal() => RunManaged(ShadowCase.GridStrideLoop);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SingleThread_LambdaLocalShadowsMethodLocal() => RunManaged(ShadowCase.SingleThread);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ForUnroll_LambdaLocalShadowsMethodLocal() => RunManaged(ShadowCase.ForUnroll);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_NestedAndLater_LambdaLocalsShadowOuterNames() => RunManaged(ShadowCase.NestedAndLater);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_LocalFunction_ShadowsAndIsShadowed() => RunManaged(ShadowCase.LocalFunction);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ManagedVersion_LambdaLocalShadowsMethodLocal() => RunManaged(ShadowCase.ManagedVersion);

    private static void RunManaged(ShadowCase shadowCase)
    {
        int[] result = [];

        ManagedKernelRun.Run(() => result = shadowCase.Launch());

        Assert.Equal(shadowCase.Expected, result);
    }
#endif

    private static void RunNative(ShadowCase shadowCase) => Assert.Equal(shadowCase.Expected, shadowCase.Launch());
}
