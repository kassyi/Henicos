#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.BoundsAndMemoryDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for null array handling, optional null arrays (#706 / #838),
/// non-nullable parameter traps, non-nullable struct fields (#1409), and simultaneous
/// multi-block null/empty trap contention.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.GlobalStateSequential)]
public class NullArrayAndFieldDifferentialTests
{
    private static readonly DifferentialCase s_nullArrayOptionalValid = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.NullArrayOptionalValidKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
            .Out<int>("dst", shape.Rows, ["opt_val"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayOptionalValidKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows,
            opt: null,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayOptionalValidKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows,
            opt: null),
    };

    private static readonly DifferentialCase s_nullArrayLengthTrap = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.NullArrayLengthTrapKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        ExpectedException = static (_, _) => NullArrayRead(337, 18),
        Inputs = static (_, shape) => new DiffBuffers()
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayLengthTrapKernel(
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayLengthTrapKernel(
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_nullArrayIndexerTrap = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.NullArrayIndexerTrapKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        ExpectedException = static (_, _) => NullArrayRead(356, 18),
        Inputs = static (_, shape) => new DiffBuffers()
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayIndexerTrapKernel(
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayIndexerTrapKernel(
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_nullArrayForeachTrap = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.NullArrayForeachTrapKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        ExpectedException = static (_, _) => NullArrayRead(376, 27),
        Inputs = static (_, shape) => new DiffBuffers()
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayForeachTrapKernel(
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayForeachTrapKernel(
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_nullArrayReassignedParameterTrap = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.NullArrayReassignedParameterTrapKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        ExpectedException = static (_, _) => NullArrayRead(405, 18),
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayReassignedParameterTrapKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayReassignedParameterTrapKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_allThreadsNullDereferenceTrap = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.AllThreadsNullDereferenceTrapKernel)))
    {
        Shape = new LaunchShape(2, 2, 2, 4, 4, 2, SimultaneousRows),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExpectedException = static (rng, _) => rng.Seed == 1 ? NullArrayRead(568, 22) : null,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var flags = new int[rows];
            if (rng.Seed == 1)
            {
                Array.Fill(flags, 1);
            }

            return new DiffBuffers()
                .In("flags", flags, readOnly: true)
                .Out<int>("dst", rows, ["trap_res"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchAllThreadsNullDereferenceTrapKernel(
            b.Get<int>("flags"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            gridDimY: s.GridY,
            gridDimZ: s.GridZ,
            blockDimX: s.BlockX,
            blockDimY: s.BlockY,
            blockDimZ: s.BlockZ),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.AllThreadsNullDereferenceTrapKernel(
            b.Get<int>("flags"),
            b.Get<int>("dst"),
            s.Rows),
    };

    // A null read in a device function reports no site on CUDA (its message is not registered),
    // so only the type is checked
    private static readonly DifferentialCase s_gridStrideNullParameter = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.GridStrideNullParameterKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        ExpectedException = static (rng, _) => rng.Seed == 1
            ? new ExceptionPolicy(typeof(NullReferenceException), DiffPaths.CheckedPaths())
            : null,
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
            .In("flags", new[] { rng.Seed }, readOnly: true)
            .Out<int>("dst", shape.Rows, ["doubled"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchGridStrideNullParameterKernel(
            b.Get<int>("src"),
            b.Get<int>("flags"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.GridStrideNullParameterKernel(
            b.Get<int>("src"),
            b.Get<int>("flags"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_allThreadsEmptyFirstTrap = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.AllThreadsEmptyFirstTrapKernel)))
    {
        Shape = new LaunchShape(2, 2, 2, 4, 4, 2, SimultaneousRows),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var emptyArr = new int[4];
            var flags = new int[rows];
            if (rng.Seed == 1)
            {
                Array.Fill(flags, 1);
            }

            return new DiffBuffers()
                .In("emptyArr", emptyArr, readOnly: true)
                .In("flags", flags, readOnly: true)
                .Out<int>("dst", rows, ["trap_res"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchAllThreadsEmptyFirstTrapKernel(
            b.Get<int>("emptyArr"),
            b.Get<int>("flags"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            gridDimY: s.GridY,
            gridDimZ: s.GridZ,
            blockDimX: s.BlockX,
            blockDimY: s.BlockY,
            blockDimZ: s.BlockZ),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.AllThreadsEmptyFirstTrapKernel(
            b.Get<int>("emptyArr"),
            b.Get<int>("flags"),
            b.Get<int>("dst"),
            s.Rows),
    };

    [SkippableFact]
    public void Test_NullArrayOptionalValid_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayOptionalValid);

    [SkippableFact]
    public void Test_NullArrayLengthTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayLengthTrap);

    [SkippableFact]
    public void Test_NullArrayIndexerTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayIndexerTrap);

    [SkippableFact]
    public void Test_NullArrayForeachTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayForeachTrap);

    [SkippableFact]
    public void Test_NullArrayReassignedParameterTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayReassignedParameterTrap);

    [SkippableFact]
    public void Test_AllThreadsNullDereferenceTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_allThreadsNullDereferenceTrap);

    // A nullable parameter keeps its check: seed 1 reads null in the grid-stride loop and throws
    [SkippableFact]
    public void Test_GridStrideNullParameter_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_gridStrideNullParameter);

    // A null passed to a non-nullable parameter: CUDA and the native build throw at the call
    // (the place of the argument on CUDA), the managed path at the read in the callee,
    // as C# does; the type is the same
    [SkippableFact]
    public void Test_NonNullableParameterNullArgument_ThrowsNullReferenceException() =>
        DifferentialRunner.Run(new DifferentialCase(
            KernelName(nameof(BoundsAndMemoryDifferentialKernels.NonNullableParameterNullArgumentKernel)))
        {
            Shape = LaunchShapes.Linear(DefaultRows, 32),
            Seeds = [0, 1],
            ExpectedException = static (rng, _) => rng.Seed == 1 ? NullArrayArgument(637, 25) : null,
            Inputs = static (rng, shape) => new DiffBuffers()
                .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
                .In("flags", new[] { rng.Seed }, readOnly: true)
                .Out<int>("dst", shape.Rows, ["read"], OutputPolicy.Exact),
            Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNonNullableParameterNullArgumentKernel(
                b.Get<int>("src"),
                b.Get<int>("flags"),
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX),
            Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NonNullableParameterNullArgumentKernel(
                b.Get<int>("src"),
                b.Get<int>("flags"),
                b.Get<int>("dst"),
                s.Rows),
        });

    // The launchers reject a null in a non-nullable array field of a struct argument before the launch (#1409); a
    // non-null one runs as the oracle
    [SkippableFact]
    public void Test_NonNullableField_NullIsRejectedAtTheLaunch() =>
        DifferentialRunner.Run(new DifferentialCase(
            KernelName(nameof(BoundsAndMemoryDifferentialKernels.NonNullableFieldKernel)))
        {
            Shape = LaunchShapes.Linear(DefaultRows, 32),
            Seeds = [0, 1],
            ExpectedException = static (rng, _) => rng.Seed == 1
                ? new ExceptionPolicy(typeof(ArgumentNullException), DiffPaths.CheckedPaths()) { ParamName = "holder" }
                : null,
            Inputs = static (rng, shape) => new DiffBuffers()
                .InDeepScalar(
                    "holder",
                    new DiffNonNullableHolder
                    {
                        // Deliberately null in a non-nullable field for seed 1: the launch rejects it
                        Data = rng.Seed == 1 ? null! : DiffValues.Int32(rng, 8),
                        Offset = 3,
                    },
                    // The clone keeps the deliberate null of seed 1 in the non-nullable field
                    static (src, scope) => new DiffNonNullableHolder
                    {
                        Data = scope.CloneArray(src.Data)!,
                        Offset = src.Offset,
                    })
                .Out<int>("dst", shape.Rows, ["value"], OutputPolicy.Exact),
            Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNonNullableFieldKernel(
                b.GetScalar<DiffNonNullableHolder>("holder"),
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX),
            Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NonNullableFieldKernel(
                b.GetScalar<DiffNonNullableHolder>("holder"),
                b.Get<int>("dst"),
                s.Rows),
        });

    [SkippableFact]
    public void Test_AllThreadsEmptyFirstTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_allThreadsEmptyFirstTrap);
}
