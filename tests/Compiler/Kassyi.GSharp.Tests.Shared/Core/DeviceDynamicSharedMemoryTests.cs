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
        Skip.If(IsCudaDialect, "#439");
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
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GrowingHelper_AfterCallerDeclaration_PreservesBothBuffers()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        DeviceDynamicSharedMemoryScenarios.GrowingHelperAfterCallerDeclaration(s_runNative);
    }
#endif

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_RecursiveHelper_SharesOneBufferAndDoesNotOverlapCaller()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        Skip.If(IsCudaDialect, "#439");
        DeviceDynamicSharedMemoryScenarios.RecursiveHelperSharesOneBufferAndDoesNotOverlapCaller(s_runNative);
    }
}
