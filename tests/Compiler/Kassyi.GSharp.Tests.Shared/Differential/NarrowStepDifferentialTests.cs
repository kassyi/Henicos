#nullable enable
using System;
using System.Runtime.CompilerServices;
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
        Int32ToInt64Launch launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: static (rng, rows) => new DiffBuffers()
                .In("a", DiffValues.Mixed(rng, rows, s_narrowStepBoundaries, static r => unchecked((int)r.NextUInt64())))
                .Out<long>("r", rows, NarrowStepDifferentialKernels.NarrowStepColumns, OutputPolicy.Exact),
            shape: LaunchShape.ForRows(32, blockX: 32),
            inputName: "a",
            outputName: "r",
            launchExpression: launchExpression);

    internal static readonly DifferentialCase s_sbyteStep = CreateStepCase(
        NarrowStepDifferentialKernels.LaunchSByteStepKernel);

    internal static readonly DifferentialCase s_byteStep = CreateStepCase(
        NarrowStepDifferentialKernels.LaunchByteStepKernel);

    internal static readonly DifferentialCase s_int16Step = CreateStepCase(
        NarrowStepDifferentialKernels.LaunchInt16StepKernel);

    internal static readonly DifferentialCase s_uint16Step = CreateStepCase(
        NarrowStepDifferentialKernels.LaunchUInt16StepKernel);

    internal static readonly DifferentialCase s_charStep = CreateStepCase(
        NarrowStepDifferentialKernels.LaunchCharStepKernel);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NarrowStep_SByte_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_sbyteStep);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NarrowStep_Byte_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_byteStep);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NarrowStep_Int16_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_int16Step);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NarrowStep_UInt16_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_uint16Step);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NarrowStep_Char_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_charStep);
}
