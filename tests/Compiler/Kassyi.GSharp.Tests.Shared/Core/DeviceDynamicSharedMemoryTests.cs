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
    // fail. The test runs only when this variable is 1, in a process of its own (#439).
    private const string TrapTestVariable = "GSHARP_TEST_CUDA_TRAP";

    [SkippableTheory]
    [InlineData(-1)]
    [InlineData(-32)]
    [Scenario(TestScenario.SadPath)]
    public void Test_NegativeCount_AfterRegion_Traps(int negative)
    {
        Skip.IfNot(Environment.GetEnvironmentVariable(TrapTestVariable) == "1",
            $"Traps and leaves the CUDA context unusable; set {TrapTestVariable}=1 and run it alone");
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[DeviceDynamicSharedMemoryScenarios.Length];

        Assert.Throws<Kassyi.GSharp.Runtime.Interop.CudaException>(() =>
            DeviceDynamicSharedMemoryKernels.LaunchNegativeCountKernel(output, DeviceDynamicSharedMemoryScenarios.Length,
                negative, blockDimX: DeviceDynamicSharedMemoryScenarios.Length,
                sharedMemBytes: 3 * DeviceDynamicSharedMemoryScenarios.Length * sizeof(int)));
    }
#endif
}
