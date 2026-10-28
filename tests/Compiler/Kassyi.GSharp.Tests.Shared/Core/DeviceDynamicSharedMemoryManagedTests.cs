#if GSHARP_CPU
using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Managed fallback execution of <see cref="DeviceDynamicSharedMemoryKernels"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.GlobalStateSequential)]
public class DeviceDynamicSharedMemoryManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SuspendingHelper_DoesNotOverlapCaller() =>
        DeviceDynamicSharedMemoryScenarios.SuspendingHelperDoesNotOverlapCaller(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_HelperReachedFromTwoKernels_GetsItsOwnBufferInEach() =>
        DeviceDynamicSharedMemoryScenarios.HelperReachedFromTwoKernels(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_PlainHelper_DoesNotOverlapCaller() =>
        DeviceDynamicSharedMemoryScenarios.PlainHelperDoesNotOverlapCaller(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_SuspendingHelper_PersistsAcrossCalls() =>
        DeviceDynamicSharedMemoryScenarios.SuspendingHelperPersistsAcrossCalls(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_PlainHelper_PersistsAcrossCalls() =>
        DeviceDynamicSharedMemoryScenarios.PlainHelperPersistsAcrossCalls(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_StaticHelper_PersistsAcrossCalls() =>
        DeviceDynamicSharedMemoryScenarios.StaticHelperPersistsAcrossCalls(s_runManaged);

    // CUDA traps when a region that is not the last one grows; the managed path throws, as the native one (#544, C10)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Test_Managed_GrowingHelper_AfterCallerDeclaration_Throws() =>
        DeviceDynamicSharedMemoryScenarios.GrowingHelperAfterCallerDeclarationThrows(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_RecursiveHelper_SharesOneBufferAndDoesNotOverlapCaller() =>
        DeviceDynamicSharedMemoryScenarios.RecursiveHelperSharesOneBufferAndDoesNotOverlapCaller(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_NestedScopeDeclarations_DoNotOverlap() =>
        DeviceDynamicSharedMemoryScenarios.NestedScopeDeclarationsDoNotOverlap(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ZeroCountDeclaration_GrowsInPlace() =>
        DeviceDynamicSharedMemoryScenarios.ZeroCountDeclarationGrowsInPlace(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_HelperBehindFunctionWithoutDeclaration_DoesNotOverlapCaller() =>
        DeviceDynamicSharedMemoryScenarios.HelperBehindFunctionWithoutDeclaration(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_HelperWithoutParameters_GetsItsBuffer() =>
        DeviceDynamicSharedMemoryScenarios.HelperWithoutParameters(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_HelpersCalledFromLocalFunctionAndLambda_DoNotOverlapCaller() =>
        DeviceDynamicSharedMemoryScenarios.HelpersCalledFromLocalFunctionAndLambda(s_runManaged);

    private static void RunManaged(Action launch)
    {
        var failures = new ConcurrentQueue<Exception>();
        var thread = new Thread(() =>
        {
            try
            {
                CpuKernelLoader.ForceManagedExecution = true;
                launch();
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
            finally
            {
                CpuKernelLoader.ForceManagedExecution = false;
            }
        });
        thread.Start();
        thread.Join();

        if (failures.TryDequeue(out var failure))
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
#endif
