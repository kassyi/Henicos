#if GSHARP_CPU
using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

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
    public void SuspendingHelper_ManagedExecution_DoesNotOverlapCaller() =>
        DeviceDynamicSharedMemoryScenarios.SuspendingHelperDoesNotOverlapCaller(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HelperReachedFromTwoKernels_ManagedExecution_GetsOwnBufferInEach() =>
        DeviceDynamicSharedMemoryScenarios.HelperReachedFromTwoKernels(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlainHelper_ManagedExecution_DoesNotOverlapCaller() =>
        DeviceDynamicSharedMemoryScenarios.PlainHelperDoesNotOverlapCaller(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SuspendingHelper_ManagedExecution_PersistsAcrossCalls() =>
        DeviceDynamicSharedMemoryScenarios.SuspendingHelperPersistsAcrossCalls(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PlainHelper_ManagedExecution_PersistsAcrossCalls() =>
        DeviceDynamicSharedMemoryScenarios.PlainHelperPersistsAcrossCalls(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void StaticHelper_ManagedExecution_PersistsAcrossCalls() =>
        DeviceDynamicSharedMemoryScenarios.StaticHelperPersistsAcrossCalls(s_runManaged);

    // CUDA traps when a region that is not the last one grows; the managed path throws, as the native one (#544, C10)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GrowingHelper_AfterCallerDeclarationManaged_ThrowsInvalidOperationException() =>
        DeviceDynamicSharedMemoryScenarios.GrowingHelperAfterCallerDeclarationThrows(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RecursiveHelper_ManagedExecution_SharesOneBufferAndDoesNotOverlapCaller() =>
        DeviceDynamicSharedMemoryScenarios.RecursiveHelperSharesOneBufferAndDoesNotOverlapCaller(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NestedScopeDeclarations_ManagedExecution_DoNotOverlap() =>
        DeviceDynamicSharedMemoryScenarios.NestedScopeDeclarationsDoNotOverlap(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroCountDeclaration_ManagedExecution_GrowsInPlace() =>
        DeviceDynamicSharedMemoryScenarios.ZeroCountDeclarationGrowsInPlace(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void HelperBehindFunctionWithoutDeclaration_ManagedExecution_DoesNotOverlapCaller() =>
        DeviceDynamicSharedMemoryScenarios.HelperBehindFunctionWithoutDeclaration(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void HelperWithoutParameters_ManagedExecution_GetsItsBuffer() =>
        DeviceDynamicSharedMemoryScenarios.HelperWithoutParameters(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void HelpersCalledFromLocalFunctionAndLambda_ManagedExecution_DoNotOverlapCaller() =>
        DeviceDynamicSharedMemoryScenarios.HelpersCalledFromLocalFunctionAndLambda(s_runManaged);

    private static void RunManaged(Action launch)
    {
        var failures = new ConcurrentQueue<Exception>();
        var thread = new Thread(() =>
        {
            try
            {
                using (GpuExecution.Use(KernelExecution.Managed))
                {
                    launch();
                }
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
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
