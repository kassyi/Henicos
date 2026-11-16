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
public partial class DeviceDynamicSharedMemoryTests : TranspilerTestBase
{
    protected static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SuspendingHelper_NativeExecution_DoesNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.SuspendingHelperDoesNotOverlapCaller(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void HelperReachedFromTwoKernels_NativeExecution_GetsOwnBufferInEach()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.HelperReachedFromTwoKernels(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void PlainHelper_NativeExecution_DoesNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.PlainHelperDoesNotOverlapCaller(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SuspendingHelper_NativeExecution_PersistsAcrossCalls()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.SuspendingHelperPersistsAcrossCalls(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void PlainHelper_NativeExecution_PersistsAcrossCalls()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.PlainHelperPersistsAcrossCalls(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void StaticHelper_NativeExecution_PersistsAcrossCalls()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.StaticHelperPersistsAcrossCalls(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RecursiveHelper_NativeExecution_SharesOneBufferAndDoesNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.RecursiveHelperSharesOneBufferAndDoesNotOverlapCaller(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NestedScopeDeclarations_NativeExecution_DoNotOverlap()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.NestedScopeDeclarationsDoNotOverlap(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroCountDeclaration_NativeExecution_GrowsInPlace()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.ZeroCountDeclarationGrowsInPlace(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void HelperBehindFunctionWithoutDeclaration_NativeExecution_DoesNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.HelperBehindFunctionWithoutDeclaration(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void HelperWithoutParameters_NativeExecution_GetsItsBuffer()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.HelperWithoutParameters(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void HelpersCalledFromLocalFunctionAndLambda_NativeExecution_DoNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.HelpersCalledFromLocalFunctionAndLambda(s_runNative);
    }
}
