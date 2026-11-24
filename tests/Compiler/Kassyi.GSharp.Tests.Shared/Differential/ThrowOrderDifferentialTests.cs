#nullable enable
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// A write to memory and a check that may throw in two operands of one expression happen in the C# order on every
/// path (#1786 (a throwing check in a later operand runs before an earlier call on MSVC)): the write of an earlier
/// operand is kept when a later operand throws, and a later operand writes nothing when an earlier one throws. The
/// oracle is the kernel's own C#, and the outputs are compared after the exception too. Seed 0 passes a null array,
/// seed 1 a populated one.
/// </summary>
/// <remarks>
/// Only the null check is exercised. An index out of range orders the same way in C# (a simple assignment checks the
/// bounds of its element when it stores, a compound assignment and a deconstruction before the right side), but the
/// native path does not check bounds yet, nor division by zero or <c>Nullable&lt;T&gt;.Value</c> (#718 (array and span
/// bounds and similar are not checked as in C#)): the out-of-range cases belong here once #718 is fixed.
/// </remarks>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Scenario(TestScenario.SadPath)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class ThrowOrderDifferentialTests
{
    private const int K0 = 0;

    internal static readonly DifferentialCase[] s_cases =
    [
        WithArray(ThrowOrderDifferentialKernels.LaunchCallThenLengthKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchAssignmentThenLengthKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchIncrementThenLengthKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchCallThenLengthArgumentKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchCallThenCheckedArgumentKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchCallThenNotNullKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchIndexCallThenCompoundLengthKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchIndexCallThenAssignedLengthKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchLengthThenCallKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchLengthThenAssignmentKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchLengthThenIncrementKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchLengthArgumentThenCallKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchIndexLengthThenCompoundCallKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchIndexLengthThenAssignedCallKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchCheckedArgumentThenCallKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchNullElementThenAssignedCallKernel),
        WithArray(ThrowOrderDifferentialKernels.LaunchDeconstructionElementThenCallKernel),
        WithStructs(ThrowOrderDifferentialKernels.LaunchStructElementThenAssignedCallKernel),
        WithStructs(ThrowOrderDifferentialKernels.LaunchStructReadIndexElementThenAssignedCallKernel),
        WithStructs(ThrowOrderDifferentialKernels.LaunchDeconstructionStructElementThenCallKernel),
        WithoutNull(ThrowOrderDifferentialKernels.LaunchElementThenAssignedCallKernel),
        WithoutNull(ThrowOrderDifferentialKernels.LaunchReadIndexElementThenAssignedCallKernel),
        WithoutNull(ThrowOrderDifferentialKernels.LaunchElementThenCompoundCallKernel),
    ];

    public static TheoryData<string> CaseNames => [.. s_cases.Select(static c => ShortName(c.Name))];

    [SkippableTheory]
    [MemberData(nameof(CaseNames))]
    public void ThrowOrder_WriteAndCheckInTwoOperands_KeepTheWritesOfCSharp(string caseName) =>
        DifferentialRunner.Run(s_cases.Single(c => ShortName(c.Name) == caseName));

    // ThrowOrderDifferentialKernels.CallThenLengthKernel -> CallThenLengthKernel
    private static string ShortName(string name) => name[(nameof(ThrowOrderDifferentialKernels).Length + 1)..];

    // a: null on seed 0, [7] on seed 1
    private static DifferentialCase WithArray(
        Int32NullablePairLaunch launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.CreateNullablePair(
            launch,
            inputs: static (rng, _) => new DiffBuffers()
                .In("o", new[] { -1, -1 }, OutputPolicy.Exact)
                .InNullable("a", rng.Seed == 0 ? null : new[] { 7 }, OutputPolicy.Exact, readOnly: true),
            shape: LaunchShapes.SingleThread(),
            firstName: "o",
            nullableSecondName: "a",
            seeds: [0, 1],
            oracleExceptions: OracleExceptionMode.MatchOracle,
            launchExpression: launchExpression);

    // a: null on seed 0, [7] on seed 1
    private static DifferentialCase WithArray(
        DiffLaunch<int[], int[]?, int> launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Create(launchExpression, [0, 1], (b, s) => launch(
            b.Get<int>("o"),
            b.Get<int>("isNull")[0] == 1 ? null : [7],
            K0,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, 0, default, default));

    // s: null on seed 0, one pair on seed 1
    private static DifferentialCase WithStructs(
        DiffLaunch<int[], ThrowOrderPair[]?, int> launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Create(launchExpression, [0, 1], (b, s) => launch(
            b.Get<int>("o"),
            b.Get<int>("isNull")[0] == 1 ? null : [new ThrowOrderPair()],
            K0,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, 0, default, default));

    // No null input: the form checks an element of o, in range (#718)
    private static DifferentialCase WithoutNull(
        Int32ScalarLaunch launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        Create(launchExpression, [1], (b, s) => launch(
            b.Get<int>("o"),
            K0,
            s.GridX, s.GridY, s.GridZ, s.BlockX, s.BlockY, s.BlockZ, 0, default, default));

    // The case of one kernel: o starts as {-1, -1}, and seed 0 asks the launch for a null array
    private static DifferentialCase Create(
        string launchExpression, int[] seeds, Action<DiffBuffers, LaunchShape> launch) =>
        new(DiffKernelNames.FromLaunchExpression(launchExpression, paramName: nameof(launch)))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = seeds,
            OracleExceptions = OracleExceptionMode.MatchOracle,
            Inputs = static (rng, _) => new DiffBuffers()
                .In("o", new[] { -1, -1 }, OutputPolicy.Exact)
                .In("isNull", new[] { rng.Seed == 0 ? 1 : 0 }, OutputPolicy.Exact, readOnly: true),
            Launch = launch,
            OracleIsLaunch = true,
        };
}
