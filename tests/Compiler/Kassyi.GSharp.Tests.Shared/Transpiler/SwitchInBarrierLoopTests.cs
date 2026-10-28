#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// Runs <see cref="SwitchInBarrierLoopKernels"/> (#670) with one block of 12 threads. On the CPU dialects each test runs
/// on the managed path and on the default path (the native library when it exists). No thread reads what another wrote,
/// so the expected values are the kernels run as plain C#, one thread at a time.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class SwitchInBarrierLoopTests : TranspilerTestBase
{
    private const int Threads = 12;

    // A goto case written for the continue would bind to the user's switch: to one of its cases 1 to 4 here
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ContinueInSwitch_ContinuesTheLoop(bool forceManaged)
    {
        var a = new int[Threads];
        var mode = Modes(6);

        Run(forceManaged, () => SwitchInBarrierLoopKernels.LaunchContinueInSwitchKernel(a, mode, blockDimX: Threads));

        Assert.Equal(mode.Select(LoopWithSwitch), a);
    }

    // A goto case written for the continue names no case of the user's switch: the managed C# or the C++ does not build
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ContinueInSwitchWithoutMatchingCase_ContinuesTheLoop(bool forceManaged)
    {
        var a = new int[Threads];
        var mode = Modes(6);

        Run(forceManaged, () => SwitchInBarrierLoopKernels.LaunchContinueInSwitchWithoutCaseKernel(a, mode, blockDimX: Threads));

        Assert.Equal(mode.Select(static m => m == 0 ? 0 : 3), a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_GotoOutOfSwitch_JumpsToTheLabelAfterTheLoop(bool forceManaged)
    {
        var a = new int[Threads];
        var mode = Modes(6);

        Run(forceManaged, () => SwitchInBarrierLoopKernels.LaunchGotoOutOfSwitchKernel(a, mode, blockDimX: Threads));

        // The goto leaves the loop in its last iteration, after two += 1, and skips the += 5 after the loop
        Assert.Equal(mode.Select(static m => m == 0 ? 2 + 7 : LoopWithSwitch(m) + 5 + 7), a);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ContinueInNestedSwitch_ContinuesTheDoLoop(bool forceManaged)
    {
        var a = new int[Threads];
        var mode = Modes(4);

        Run(forceManaged, () => SwitchInBarrierLoopKernels.LaunchContinueInNestedSwitchKernel(a, mode, blockDimX: Threads));

        // Mode 0 continues every iteration; 1 and 3 add 10, 2 adds 100, each followed by 1, three times
        Assert.Equal(mode.Select(static m => m switch { 0 => 0, 2 => 303, _ => 33 }), a);
    }

    // The loop of ContinueInSwitchKernel as plain C#, for one thread of the given mode
    private static int LoopWithSwitch(int mode)
    {
        var a = 0;
        for (var i = 0; i < 3; i++)
        {
            switch (mode)
            {
                case 0:
                    continue;
                case 1:
                    a += 10;
                    break;
                case 2:
                    a += 20;
                    break;
                case 3:
                    a += 100;
                    break;
                case 4:
                    a += 1000;
                    break;
            }

            a += 1;
        }

        return a;
    }

    private static int[] Modes(int count) => Enumerable.Range(0, Threads).Select(t => t % count).ToArray();

    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
