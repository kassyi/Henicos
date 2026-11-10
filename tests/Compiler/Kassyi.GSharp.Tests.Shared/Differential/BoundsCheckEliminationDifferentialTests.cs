#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.BoundsAndMemoryDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for Charter §4 bounds-check over-elimination witnesses.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.BoundsAndMemoryDifferential)]
public class BoundsCheckEliminationDifferentialTests
{
    private static readonly DifferentialCase s_boundsCheckEliminationValid = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationValidKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (_, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % CanaryLen;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationValidKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationValidKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    private static readonly DifferentialCase s_boundsCheckEliminationOffByOne = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationOffByOneKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % CanaryLen;
            }

            if (rng.Seed == 1)
            {
                indices[1] = CanaryLen;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationOffByOneKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationOffByOneKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    private static readonly DifferentialCase s_boundsCheckEliminationWrongSpan = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationWrongSpanKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var otherBacking = CreateCanaryBacking();
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % CanaryLen;
            }

            if (rng.Seed == 1)
            {
                indices[2] = CanaryLen;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("otherBacking", otherBacking, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationWrongSpanKernel(
            b.Get<int>("backing"),
            b.Get<int>("otherBacking"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            CanaryLen + 4,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationWrongSpanKernel(
            b.Get<int>("backing"),
            b.Get<int>("otherBacking"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            CanaryLen + 4,
            s.Rows),
    };

    private static readonly DifferentialCase s_boundsCheckEliminationPostIncrement = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationPostIncrementKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % (CanaryLen - 1);
            }

            if (rng.Seed == 1)
            {
                indices[3] = CanaryLen - 1;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationPostIncrementKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationPostIncrementKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    private static readonly DifferentialCase s_boundsCheckEliminationUpperOnly = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationUpperOnlyKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % CanaryLen;
            }

            if (rng.Seed == 1)
            {
                indices[4] = -1;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationUpperOnlyKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationUpperOnlyKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    [SkippableFact]
    public void Test_BoundsCheckEliminationValid_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationValid);

    [SkippableFact]
    public void Test_BoundsCheckEliminationOffByOne_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationOffByOne);

    [SkippableFact]
    public void Test_BoundsCheckEliminationWrongSpan_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationWrongSpan);

    [SkippableFact]
    public void Test_BoundsCheckEliminationPostIncrement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationPostIncrement);

    [SkippableFact]
    public void Test_BoundsCheckEliminationUpperOnly_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationUpperOnly);
}
