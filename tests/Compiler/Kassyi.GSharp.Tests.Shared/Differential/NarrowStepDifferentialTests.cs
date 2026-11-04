#nullable enable
using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for prefix and postfix <c>++</c> and <c>--</c> on narrow integer types
/// (<see cref="sbyte"/>, <see cref="byte"/>, <see cref="short"/>, <see cref="ushort"/>, <see cref="char"/>) (#1079).
/// </summary>
public class NarrowStepDifferentialTests
{
    private static readonly int[] s_narrowStepBoundaries =
    [
        0,
        1,
        -1,
        sbyte.MinValue,
        sbyte.MinValue + 1,
        sbyte.MaxValue - 1,
        sbyte.MaxValue,
        byte.MaxValue - 1,
        byte.MaxValue,
        short.MinValue,
        short.MinValue + 1,
        short.MaxValue - 1,
        short.MaxValue,
        ushort.MaxValue - 1,
        ushort.MaxValue,
    ];

    private static DifferentialCase CreateStepCase(
        string kernelName,
        Action<DiffBuffers, LaunchShape> launch,
        Action<DiffBuffers> oracle) =>
        new($"{nameof(NarrowStepDifferentialKernels)}.{kernelName}")
        {
            Shape = LaunchShape.ForRows(32, blockX: 32),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Mixed(rng, rows, s_narrowStepBoundaries, static r => unchecked((int)r.NextUInt64())))
                .Out<long>("r", rows, NarrowStepDifferentialKernels.NarrowStepColumns, OutputPolicy.Exact),
            Launch = launch,
            Oracle = oracle,
        };

    private static readonly DifferentialCase s_sbyteStep = CreateStepCase(
        nameof(NarrowStepDifferentialKernels.SByteStepKernel),
        static (b, s) => NarrowStepDifferentialKernels.LaunchSByteStepKernel(
            b.Get<int>("a"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        static b => NarrowStepDifferentialKernels.SByteStepKernel(
            b.Get<int>("a"),
            b.Get<long>("r")));

    private static readonly DifferentialCase s_byteStep = CreateStepCase(
        nameof(NarrowStepDifferentialKernels.ByteStepKernel),
        static (b, s) => NarrowStepDifferentialKernels.LaunchByteStepKernel(
            b.Get<int>("a"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        static b => NarrowStepDifferentialKernels.ByteStepKernel(
            b.Get<int>("a"),
            b.Get<long>("r")));

    private static readonly DifferentialCase s_int16Step = CreateStepCase(
        nameof(NarrowStepDifferentialKernels.Int16StepKernel),
        static (b, s) => NarrowStepDifferentialKernels.LaunchInt16StepKernel(
            b.Get<int>("a"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        static b => NarrowStepDifferentialKernels.Int16StepKernel(
            b.Get<int>("a"),
            b.Get<long>("r")));

    private static readonly DifferentialCase s_uint16Step = CreateStepCase(
        nameof(NarrowStepDifferentialKernels.UInt16StepKernel),
        static (b, s) => NarrowStepDifferentialKernels.LaunchUInt16StepKernel(
            b.Get<int>("a"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        static b => NarrowStepDifferentialKernels.UInt16StepKernel(
            b.Get<int>("a"),
            b.Get<long>("r")));

    private static readonly DifferentialCase s_charStep = CreateStepCase(
        nameof(NarrowStepDifferentialKernels.CharStepKernel),
        static (b, s) => NarrowStepDifferentialKernels.LaunchCharStepKernel(
            b.Get<int>("a"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        static b => NarrowStepDifferentialKernels.CharStepKernel(
            b.Get<int>("a"),
            b.Get<long>("r")));

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowStep_SByte_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_sbyteStep);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowStep_Byte_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_byteStep);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowStep_Int16_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_int16Step);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowStep_UInt16_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_uint16Step);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowStep_Char_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_charStep);
}
