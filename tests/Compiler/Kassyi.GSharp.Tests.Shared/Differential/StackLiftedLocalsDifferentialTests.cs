#if GSHARP_BOTH_STACKS
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

// The kernels of StackLiftedLocalsDifferentialKernels on every path of the test assembly (the native and managed stack
// models in Tests.Cpu, CUDA in Tests.Cuda), built with GSharpNativeBarrierModel=Stack and
// GSharpManagedBarrierModel=Stack, against values computed here from the C# of each kernel
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class StackLiftedLocalsDifferentialTests
{
    private const int Threads = StackLiftedLocalsDifferentialKernels.Threads;

    // name -> (data length, launch, expected data or null, expected output or null)
    private static readonly Dictionary<string, (int DataLength, Action<int[], int[]> Launch, Func<int[]>? Data,
        Func<int[]>? Output)> s_kernels = new()
    {
        ["SpanAcrossBarrier"] = (Threads * 2,
            static (d, o) => StackLiftedLocalsDifferentialKernels.LaunchSpanAcrossBarrierKernel(d, o,
                blockDimX: Threads),
            null,
            static () => PerThread(static t => ((t + 1) * 100) + (3 * ((t + 1) & 31)))),
        ["StackLinqChainAcrossBarrier"] = (Threads,
            static (d, o) => StackLiftedLocalsDifferentialKernels.LaunchStackLinqChainAcrossBarrierKernel(d, o,
                blockDimX: Threads),
            null,
            // The positive values t - 10 for t = 11..31 are 1..21, whose sum is 231
            static () => PerThread(static t => 231 + t)),
        ["RefLocalAcrossBarrier"] = (Threads,
            static (d, o) => StackLiftedLocalsDifferentialKernels.LaunchRefLocalAcrossBarrierKernel(d, o,
                blockDimX: Threads),
            static () => PerThread(static t => (t * 5) + (((t + 1) & 31) * 5)),
            static () => PerThread(static t => (t * 5) + (((t + 1) & 31) * 5))),
        ["SpanParameterAcrossBarrier"] = (Threads + 1,
            static (d, o) => StackLiftedLocalsDifferentialKernels.LaunchSpanParameterAcrossBarrierKernel(d, o,
                blockDimX: Threads),
            null,
            static () => PerThread(static t => t < Threads - 1 ? (t + 1) * 7 : 1000)),
        ["RefForEachAcrossBarrier"] = (Threads * 2,
            static (d, _) => StackLiftedLocalsDifferentialKernels.LaunchRefForEachAcrossBarrierKernel(d,
                blockDimX: Threads),
            // Element i starts at i and its thread i / 2 adds itself once
            static () => Enumerable.Range(0, Threads * 2).Select(static i => i + (i / 2)).ToArray(),
            null),
        ["LocalAddressAcrossBarrier"] = (0,
            static (_, o) => StackLiftedLocalsDifferentialKernels.LaunchLocalAddressAcrossBarrierKernel(o,
                blockDimX: Threads),
            null,
            static () => PerThread(static t => 2 * ((t * 3) + 1))),
        ["StackallocAcrossBarrier"] = (0,
            static (_, o) => StackLiftedLocalsDifferentialKernels.LaunchStackallocAcrossBarrierKernel(o,
                blockDimX: Threads),
            null,
            static () => PerThread(static t => (4 * t) + 6)),
    };

    public static TheoryData<string> KernelNames() => new(s_kernels.Keys.Order(StringComparer.Ordinal));

    [SkippableTheory]
    [MemberData(nameof(KernelNames))]
    [Scenario(TestScenario.HappyPath)]
    public void LocalKeptAcrossBarrier_EveryPath_GivesTheValuesOfTheCSharp(string kernel)
    {
        var (dataLength, launch, expectedData, expectedOutput) = s_kernels[kernel];
        var paths = DiffPaths.Available;
        Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            path.Preflight?.Invoke();
            // The initial data is the index, which only RefForEachAcrossBarrier reads before it writes
            var data = Enumerable.Range(0, dataLength).ToArray();
            var output = new int[Threads];
            path.Run(() => launch(data, output));
            if (expectedData is not null)
            {
                Assert.True(expectedData().SequenceEqual(data),
                    $"{kernel} on {path.Name}: data [{string.Join(", ", data)}]");
            }

            if (expectedOutput is not null)
            {
                Assert.True(expectedOutput().SequenceEqual(output),
                    $"{kernel} on {path.Name}: output [{string.Join(", ", output)}]");
            }
        }
    }

    private static int[] PerThread(Func<int, int> value) => Enumerable.Range(0, Threads).Select(value).ToArray();
}
#endif
