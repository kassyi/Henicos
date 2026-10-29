#if GSHARP_CPU
using System;
using System.Collections.Concurrent;
using System.Threading;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The <c>Gpu.Atomic*</c> family on the managed C# fallback gives the values of the native dialects (#728): the same
/// final values, and old values that one order of the operations explains (<see cref="AtomicIntrinsicsScenarios"/>).
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class AtomicIntrinsicsManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = ManagedKernelRun.Run;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_AtomicArithmetic_AllThreadsContributeToSingleAddress() =>
        AtomicIntrinsicsScenarios.Arithmetic(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0u, 0u)]
    [InlineData(0u, 9u)]
    [InlineData(0u, uint.MaxValue)]
    [InlineData(100u, 9u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void Test_Managed_AtomicIncDec_WrapsAroundLimit(uint initial, uint limit) =>
        AtomicIntrinsicsScenarios.IncDecWrap(s_runManaged, initial, limit);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_AtomicFloatAdd_ManyBlocks() => AtomicIntrinsicsScenarios.FloatAddHighContention(s_runManaged);

    // The atomic is in a device function that does not suspend, which the managed path calls as the user's C# method
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_AtomicThroughRefParameter_UpdatesCallerElement() =>
        AtomicIntrinsicsScenarios.ThroughRefParameter(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_EveryAtomicOverload_ReturnsTheOldValueOfOneOrder() =>
        AtomicIntrinsicsScenarios.OldValues(s_runManaged);

    // Two host threads run managed launches on the same array at the same time: each update is atomic, so none is lost.
    // 2 x 20 launches of 16 x 256 additions of 1 give 163840, which a float holds exactly
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ConcurrentLaunchesOnOneArray_LoseNoUpdate()
    {
        const int hostThreads = 2;
        const int launchesPerThread = 20;
        const uint threadsPerBlock = 256;
        const uint blocksPerGrid = 16;
        float[] sum = [0f];
        var failures = new ConcurrentQueue<Exception>();
        using var start = new Barrier(hostThreads);
        var threads = new Thread[hostThreads];
        for (var i = 0; i < hostThreads; i++)
        {
            threads[i] = new Thread(() =>
            {
                try
                {
                    CpuKernelLoader.ForceManagedExecution = true;
                    start.SignalAndWait();
                    for (var launch = 0; launch < launchesPerThread; launch++)
                    {
                        AtomicIntrinsicsKernels.LaunchAtomicFloatAddStressKernel(
                            sum, gridDimX: blocksPerGrid, blockDimX: threadsPerBlock);
                    }
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
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Assert.Empty(failures);
        Assert.Equal(hostThreads * launchesPerThread * threadsPerBlock * blocksPerGrid, sum[0]);
    }
}
#endif
