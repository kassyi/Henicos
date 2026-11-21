#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Differential;

namespace Kassyi.GSharp.Tests.Cpu.Runtime;

// The kernels of StackBodyLocalsKernels give the same values on every CPU path. Under the managed stack model the
// managed path runs them on the stacks (the body lowered for the stacks, with its locals moved, typed and initialized)
// while the native path runs the state machine, so the comparison checks the stack model against the state machine
[Collection(TestCollections.GlobalStateSequential)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
public class StackBodyLocalsTests
{
#if GSHARP_MANAGED_STACK
    private const bool BuiltWithStackModel = true;
#else
    private const bool BuiltWithStackModel = false;
#endif

    private const int Threads = 32;

    private static readonly Dictionary<string, Action<uint[]>> s_kernels = new()
    {
        ["AssignedInAnd"] = static r => StackBodyLocalsKernels.LaunchAssignedInAndKernel(r, blockDimX: Threads),
        ["AssignedInOr"] = static r => StackBodyLocalsKernels.LaunchAssignedInOrKernel(r, blockDimX: Threads),
        ["OutVarInAnd"] = static r => StackBodyLocalsKernels.LaunchOutVarInAndKernel(r, blockDimX: Threads),
        ["OutVarStatement"] = static r => StackBodyLocalsKernels.LaunchOutVarStatementKernel(r, blockDimX: Threads),
        ["OutVarCondition"] = static r => StackBodyLocalsKernels.LaunchOutVarConditionKernel(r, blockDimX: Threads),
        ["PredeclaredOutInAnd"] = static r =>
            StackBodyLocalsKernels.LaunchPredeclaredOutInAndKernel(r, blockDimX: Threads),
        ["AssignedInTernaryCondition"] = static r =>
            StackBodyLocalsKernels.LaunchAssignedInTernaryConditionKernel(r, blockDimX: Threads),
        ["ReturnTernaryOutVar"] = static r =>
            StackBodyLocalsKernels.LaunchReturnTernaryOutVarKernel(r, blockDimX: Threads),
        ["NegatedConditionEarlyReturn"] = static r =>
            StackBodyLocalsKernels.LaunchNegatedConditionEarlyReturnKernel(r, blockDimX: Threads),
        ["ShuffleAssignedInAnd"] = static r =>
            StackBodyLocalsKernels.LaunchShuffleAssignedInAndKernel(r, blockDimX: Threads),
        ["AssignedInLoop"] = static r => StackBodyLocalsKernels.LaunchAssignedInLoopKernel(r, blockDimX: Threads),
        ["OutVarInSwitchSection"] = static r =>
            StackBodyLocalsKernels.LaunchOutVarInSwitchSectionKernel(r, blockDimX: Threads),
        ["OutVarStruct"] = static r => StackBodyLocalsKernels.LaunchOutVarStructKernel(r, blockDimX: Threads),
        ["OutVarAndInSwitchSection"] = static r =>
            StackBodyLocalsKernels.LaunchOutVarAndInSwitchSectionKernel(r, blockDimX: Threads),
        ["OutVarOfShuffleCallee"] = static r =>
            StackBodyLocalsKernels.LaunchOutVarOfShuffleCalleeKernel(r, blockDimX: Threads),
        ["OutVarOfBarrierCallee"] = static r =>
            StackBodyLocalsKernels.LaunchOutVarOfBarrierCalleeKernel(r, blockDimX: Threads),
    };

    public static TheoryData<string> KernelNames() => new(s_kernels.Keys.Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(KernelNames))]
    [Scenario(TestScenario.Boundary)]
    public void LocalsOutOfTheirScope_EveryCpuPath_GiveTheValuesOfTheStateMachine(string kernel)
    {
        var launch = s_kernels[kernel];
        var paths = DiffPaths.Available;
        var results = new List<(string Path, uint[] Values)>();
        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            var output = new uint[Threads];
            // The managed path launches on a thread of its own (ManagedKernelRun), so the count is read there
            long launches = 0;
            path.Run(() =>
            {
                long before = ManagedStackRunner.LaunchCountOnThisThread;
                launch(output);
                launches = ManagedStackRunner.LaunchCountOnThisThread - before;
            });
            if (path.Path == DiffPath.Managed)
            {
                Assert.Equal(BuiltWithStackModel ? 1L : 0L, launches);
            }

            results.Add((path.Name, output));
        }

        Assert.Contains(results, static r => r.Path == DiffPaths.Format(DiffPath.Managed));
        Assert.Contains(results, static r => r.Path == DiffPaths.Format(DiffPath.Native));
        foreach (var (path, values) in results.Skip(1))
        {
            Assert.True(results[0].Values.SequenceEqual(values),
                $"{kernel}: {results[0].Path} [{Format(results[0].Values)}], {path} [{Format(values)}]");
        }
    }

    private static string Format(uint[] values) => string.Join(", ", values.Select(static v => $"0x{v:X}"));
}
