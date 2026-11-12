#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="Gpu"/> shared-memory atomics across block threads and multi-stage reductions.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.AtomicsDifferential)]
public class AtomicsSharedMemoryDifferentialTests
{
    internal static readonly DifferentialCase s_integerSharedMemoryTwoStageAtomicsInt32 =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.IntegerSharedMemoryTwoStageAtomicsInt32Kernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(128, blockX: 64),
            ],
            Inputs = static (rng, s) => new DiffBuffers()
                .In("input", DiffValues.Int32(rng, s.ThreadCount), OutputPolicy.Exact, readOnly: true)
                .Out<int>(
                    "threadOutput",
                    s.ThreadCount,
                    AtomicsDifferentialKernels.SharedTwoStageThreadInt32Columns,
                    OutputPolicy.Exact)
                .In("globalSummary", [0, int.MaxValue, 0, 0], OutputPolicy.Exact),
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerSharedMemoryTwoStageAtomicsInt32Kernel(
                b.Get<int>("input"),
                b.Get<int>("threadOutput"),
                b.Get<int>("globalSummary"),
                dynSize: 8,
                gridDimX: s.GridX,
                blockDimX: s.BlockX,
                sharedMemBytes: 8 * sizeof(int)
            ),
            Reference = AtomicsDifferentialTestHelpers.SimulateSharedMemoryTwoStageInt32,
        };

    internal static readonly DifferentialCase s_integerSharedMemoryTwoStageAtomicsUInt32 =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.IntegerSharedMemoryTwoStageAtomicsUInt32Kernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(128, blockX: 64),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inUInt = new uint[rows];
                for (int i = 0; i < rows; i++)
                {
                    inUInt[i] = rng.NextUInt32();
                }

                return new DiffBuffers()
                    .In("input", inUInt, OutputPolicy.Exact, readOnly: true)
                    .Out<uint>(
                        "threadOutput",
                        rows,
                        AtomicsDifferentialKernels.SharedTwoStageThreadUInt32Columns,
                        OutputPolicy.Exact)
                    .In("globalSummary", [0u], OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerSharedMemoryTwoStageAtomicsUInt32Kernel(
                b.Get<uint>("input"),
                b.Get<uint>("threadOutput"),
                b.Get<uint>("globalSummary"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = AtomicsDifferentialTestHelpers.SimulateSharedMemoryTwoStageUInt32,
        };

    internal static readonly DifferentialCase s_integerSharedMemoryTwoStageAtomicsUInt64 =
        new(
            $"{nameof(AtomicsDifferentialKernels)}." +
            $"{nameof(AtomicsDifferentialKernels.IntegerSharedMemoryTwoStageAtomicsUInt64Kernel)}")
        {
            Shapes =
            [
                LaunchShape.ForRows(64, blockX: 32),
                LaunchShape.ForRows(128, blockX: 64),
            ],
            Inputs = static (rng, s) =>
            {
                int rows = s.ThreadCount;
                var inULong = new ulong[rows];
                for (int i = 0; i < rows; i++)
                {
                    inULong[i] = ((ulong)rng.NextUInt32() << 32) | rng.NextUInt32();
                }

                return new DiffBuffers()
                    .In("input", inULong, OutputPolicy.Exact, readOnly: true)
                    .Out<ulong>(
                        "threadOutput",
                        rows,
                        AtomicsDifferentialKernels.SharedTwoStageThreadUInt64Columns,
                        OutputPolicy.Exact)
                    .In("globalSummary", [0UL], OutputPolicy.Exact);
            },
            Launch = static (b, s) => AtomicsDifferentialKernels.LaunchIntegerSharedMemoryTwoStageAtomicsUInt64Kernel(
                b.Get<ulong>("input"),
                b.Get<ulong>("threadOutput"),
                b.Get<ulong>("globalSummary"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            Reference = AtomicsDifferentialTestHelpers.SimulateSharedMemoryTwoStageUInt64,
        };

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedMemoryAtomics_TwoStageInt32_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integerSharedMemoryTwoStageAtomicsInt32);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedMemoryAtomics_TwoStageUInt32_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integerSharedMemoryTwoStageAtomicsUInt32);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SharedMemoryAtomics_TwoStageUInt64_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_integerSharedMemoryTwoStageAtomicsUInt64);
}
