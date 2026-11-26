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
[Collection(TestCollections.BoundsAndMemoryDifferential)]
public class NullArrayAndFieldDifferentialTests
{
    internal static readonly DifferentialCase s_nullArrayOptionalValid = new(
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
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nullArrayLengthTrap = DiffCases.Create(
        BoundsAndMemoryDifferentialKernels.LaunchNullArrayLengthTrapKernel,
        inputs: static (_, shape) => new DiffBuffers()
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        shape: LaunchShapes.Linear(DefaultRows, 32),
        bufferName: "dst",
        expectedException: static (_, _) => NullArrayRead(353, 18));

    internal static readonly DifferentialCase s_nullArrayIndexerTrap = DiffCases.Create(
        BoundsAndMemoryDifferentialKernels.LaunchNullArrayIndexerTrapKernel,
        inputs: static (_, shape) => new DiffBuffers()
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        shape: LaunchShapes.Linear(DefaultRows, 32),
        bufferName: "dst",
        expectedException: static (_, _) => NullArrayRead(372, 18));

    internal static readonly DifferentialCase s_nullArrayForeachTrap = DiffCases.Create(
        BoundsAndMemoryDifferentialKernels.LaunchNullArrayForeachTrapKernel,
        inputs: static (_, shape) => new DiffBuffers()
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        shape: LaunchShapes.Linear(DefaultRows, 32),
        bufferName: "dst",
        expectedException: static (_, _) => NullArrayRead(392, 27));

    internal static readonly DifferentialCase s_nullArrayReassignedParameterTrap = DiffCases.Create(
        BoundsAndMemoryDifferentialKernels.LaunchNullArrayReassignedParameterTrapKernel,
        inputs: static (rng, shape) => new DiffBuffers()
            .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        shape: LaunchShapes.Linear(DefaultRows, 32),
        inputName: "src",
        expectedException: static (_, _) => NullArrayRead(421, 18));

    internal static readonly DifferentialCase s_allThreadsNullDereferenceTrap = DiffCases.Create(
        BoundsAndMemoryDifferentialKernels.LaunchAllThreadsNullDereferenceTrapKernel,
        inputs: static (rng, shape) =>
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
        shape: new LaunchShape(2, 2, 2, 4, 4, 2, SimultaneousRows),
        inputName: "flags",
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle,
        expectedException: static (rng, _) => rng.Seed == 1 ? NullArrayRead(599, 22) : null);

    // A null read in a device function reports no site on CUDA (its message is not registered),
    // so only the type is checked
    internal static readonly DifferentialCase s_gridStrideNullParameter = DiffCases.Create(
        BoundsAndMemoryDifferentialKernels.LaunchGridStrideNullParameterKernel,
        inputs: static (rng, shape) => new DiffBuffers()
            .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
            .In("flags", new[] { rng.Seed }, readOnly: true)
            .Out<int>("dst", shape.Rows, ["doubled"], OutputPolicy.Exact),
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "src",
        secondInputName: "flags",
        seeds: [0, 1],
        expectedException: static (rng, _) => rng.Seed == 1
            ? new ExceptionPolicy(typeof(NullReferenceException), DiffPaths.CheckedPaths())
            : null);

    internal static readonly DifferentialCase s_allThreadsEmptyFirstTrap = DiffCases.Create(
        BoundsAndMemoryDifferentialKernels.LaunchAllThreadsEmptyFirstTrapKernel,
        inputs: static (rng, shape) =>
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
        shape: new LaunchShape(2, 2, 2, 4, 4, 2, SimultaneousRows),
        firstInputName: "emptyArr",
        secondInputName: "flags",
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    [SkippableFact]
    public void NullArray_OptionalValid_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayOptionalValid);

    [SkippableFact]
    public void NullArray_LengthTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayLengthTrap);

    [SkippableFact]
    public void NullArray_IndexerTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayIndexerTrap);

    [SkippableFact]
    public void NullArray_ForeachTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayForeachTrap);

    [SkippableFact]
    public void NullArray_ReassignedParameterTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayReassignedParameterTrap);

    [SkippableFact]
    public void NullDereference_AllThreadsTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_allThreadsNullDereferenceTrap);

    // A nullable parameter keeps its check: seed 1 reads null in the grid-stride loop and throws
    [SkippableFact]
    public void NullArray_GridStrideNullParameter_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_gridStrideNullParameter);

    // A null passed to a non-nullable parameter: CUDA and the native build throw at the call
    // (the place of the argument on CUDA), the managed path at the read in the callee,
    // as C# does; the type is the same
    [SkippableFact]
    public void NullValidation_NonNullableParameterNullArgument_ThrowsNullReferenceException() =>
        DifferentialRunner.Run(DiffCases.Create(
            BoundsAndMemoryDifferentialKernels.LaunchNonNullableParameterNullArgumentKernel,
            inputs: static (rng, shape) => new DiffBuffers()
                .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
                .In("flags", new[] { rng.Seed }, readOnly: true)
                .Out<int>("dst", shape.Rows, ["read"], OutputPolicy.Exact),
            shape: LaunchShapes.Linear(DefaultRows, 32),
            firstInputName: "src",
            secondInputName: "flags",
            seeds: [0, 1],
            expectedException: static (rng, _) => rng.Seed == 1 ? NullArrayArgument(668, 25) : null));

    // A null in a non-nullable array field of a struct argument reaches the kernel, as in a C# call, and throws where
    // the kernel reads it (holder.Data.Length in the index, evaluated before the element); a non-null one runs as the
    // oracle
    [SkippableFact]
    public void NullValidation_NonNullableField_NullThrowsAtTheRead() =>
        DifferentialRunner.Run(new DifferentialCase(
            KernelName(nameof(BoundsAndMemoryDifferentialKernels.NonNullableFieldKernel)))
        {
            Shape = LaunchShapes.Linear(DefaultRows, 32),
            Seeds = [0, 1],
            ExpectedException = static (rng, _) => rng.Seed == 1 ? NullArrayRead(688, 34) : null,
            Inputs = static (rng, shape) => new DiffBuffers()
                .InDeepScalar(
                    "holder",
                    new DiffNonNullableHolder
                    {
                        // Deliberately null in a non-nullable field for seed 1: the kernel's read throws
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
            OracleIsLaunch = true,
        });

    // A struct argument of the kernel passed by value to a device function: the call checks the non-nullable field, so
    // CUDA and the native build throw at the call (the place of the argument on CUDA), the managed path at the read in
    // the callee, as C# does; the type is the same. The callee never reads the null field on CUDA
    [SkippableFact]
    public void NullValidation_NonNullableFieldPassedToAFunction_ThrowsAtTheCall() =>
        DifferentialRunner.Run(new DifferentialCase(
            KernelName(nameof(BoundsAndMemoryDifferentialKernels.NonNullableFieldToFunctionKernel)))
        {
            Shape = LaunchShapes.Linear(DefaultRows, 32),
            Seeds = [0, 1],
            ExpectedException = static (rng, _) => rng.Seed == 1 ? NullArrayArgument(722, 40) : null,
            Inputs = static (rng, shape) => new DiffBuffers()
                .InDeepScalar(
                    "holder",
                    new DiffNonNullableHolder
                    {
                        // Deliberately null in a non-nullable field for seed 1: the call checks it
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
            Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNonNullableFieldToFunctionKernel(
                b.GetScalar<DiffNonNullableHolder>("holder"),
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX),
            OracleIsLaunch = true,
        });

    [SkippableFact]
    public void NullArray_AllThreadsEmptyFirstTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_allThreadsEmptyFirstTrap);

    // Gpu.NotNull: seed 0 passes the array through, seed 1 throws NullReferenceException at the call on every path (the
    // place of the call on CUDA)
    [SkippableFact]
    public void NullValidation_GpuNotNull_ThrowsAtTheCall() =>
        DifferentialRunner.Run(DiffCases.Create(
            BoundsAndMemoryDifferentialKernels.LaunchGpuNotNullKernel,
            inputs: static (rng, shape) => new DiffBuffers()
                .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
                .In("flags", new[] { rng.Seed }, readOnly: true)
                .Out<int>("dst", shape.Rows, ["value"], OutputPolicy.Exact),
            shape: LaunchShapes.Linear(DefaultRows, 32),
            firstInputName: "src",
            secondInputName: "flags",
            seeds: [0, 1],
            expectedException: static (rng, _) => rng.Seed == 1 ? NullArrayArgument(705, 23) : null));
}
