using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// <see cref="Kassyi.GSharp.Core.SharedMemory{T}"/> declarations inside <c>[GpuDeviceFunction]</c> helpers on the
/// native dialects.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class DeviceDynamicSharedMemoryTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SuspendingHelper_DoesNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.SuspendingHelperDoesNotOverlapCaller(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_HelperReachedFromTwoKernels_GetsItsOwnBufferInEach()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.HelperReachedFromTwoKernels(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_PlainHelper_DoesNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.PlainHelperDoesNotOverlapCaller(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_SuspendingHelper_PersistsAcrossCalls()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.SuspendingHelperPersistsAcrossCalls(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_PlainHelper_PersistsAcrossCalls()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.PlainHelperPersistsAcrossCalls(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StaticHelper_PersistsAcrossCalls()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.StaticHelperPersistsAcrossCalls(s_runNative);
    }

#if GSHARP_CPU
    // CUDA traps when a region that is not the last one grows; the native CPU path throws (#544, C10)
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_GrowingHelper_AfterCallerDeclaration_Throws()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.GrowingHelperAfterCallerDeclarationThrows(s_runNative);
    }
#endif

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_RecursiveHelper_SharesOneBufferAndDoesNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.RecursiveHelperSharesOneBufferAndDoesNotOverlapCaller(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NestedScopeDeclarations_DoNotOverlap()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.NestedScopeDeclarationsDoNotOverlap(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ZeroCountDeclaration_GrowsInPlace()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.ZeroCountDeclarationGrowsInPlace(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_HelperBehindFunctionWithoutDeclaration_DoesNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.HelperBehindFunctionWithoutDeclaration(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_HelperWithoutParameters_GetsItsBuffer()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.HelperWithoutParameters(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_HelpersCalledFromLocalFunctionAndLambda_DoNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.HelpersCalledFromLocalFunctionAndLambda(s_runNative);
    }

#if GSHARP_CUDA
    // [WHY] A trap leaves the CUDA context of the process unusable, so every later CUDA test in the same testhost would
    // fail. Each case runs in a child process of its own (#439), and only in `just test-gpu-trap` (#1457).
    [SkippableFact]
    [Category(TestCategory.GpuTrap)]
    [Scenario(TestScenario.SadPath)]
    public void Test_NegativeCountMinusOne_AfterRegion_Traps() =>
        RunNegativeCountTrap(-1, nameof(Test_NegativeCountMinusOne_AfterRegion_Traps));

    [SkippableFact]
    [Category(TestCategory.GpuTrap)]
    [Scenario(TestScenario.SadPath)]
    public void Test_NegativeCountMinus32_AfterRegion_Traps() =>
        RunNegativeCountTrap(-32, nameof(Test_NegativeCountMinus32_AfterRegion_Traps));

    private void RunNegativeCountTrap(int negative, string testMethodName)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        if (!IsolatedTestProcess.IsChild(testMethodName))
        {
            CudaTrapRuns.RunIsolated(typeof(DeviceDynamicSharedMemoryTests), testMethodName);
            return;
        }

        var output = new int[DeviceDynamicSharedMemoryScenarios.Length];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceDynamicSharedMemoryKernels.LaunchNegativeCountKernel(output, DeviceDynamicSharedMemoryScenarios.Length,
                negative, blockDimX: DeviceDynamicSharedMemoryScenarios.Length,
                sharedMemBytes: 3 * DeviceDynamicSharedMemoryScenarios.Length * sizeof(int)));
        Assert.Contains("G#: dynamic shared memory exceeds sharedMemBytes", ex.Message);
        var cudaEx = Assert.IsType<Kassyi.GSharp.Runtime.Interop.CudaException>(ex.InnerException);
        Assert.True(cudaEx.Result is Kassyi.GSharp.Runtime.Interop.CUresult.CudaErrorIllegalInstruction or Kassyi.GSharp.Runtime.Interop.CUresult.CudaErrorLaunchFailed);
    }
#endif
}
