#nullable enable
using System;
using System.Linq;
using Xunit;

using K = Kassyi.GSharp.Tests.Shared.Differential.ThrowOrderDifferentialKernels;

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
        WithArray(nameof(K.CallThenLengthKernel),
            static (o, a, s) => K.LaunchCallThenLengthKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.AssignmentThenLengthKernel),
            static (o, a, s) => K.LaunchAssignmentThenLengthKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.IncrementThenLengthKernel),
            static (o, a, s) => K.LaunchIncrementThenLengthKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.CallThenLengthArgumentKernel),
            static (o, a, s) => K.LaunchCallThenLengthArgumentKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.CallThenCheckedArgumentKernel),
            static (o, a, s) => K.LaunchCallThenCheckedArgumentKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.CallThenNotNullKernel),
            static (o, a, s) => K.LaunchCallThenNotNullKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.IndexCallThenCompoundLengthKernel),
            static (o, a, s) =>
                K.LaunchIndexCallThenCompoundLengthKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.IndexCallThenAssignedLengthKernel),
            static (o, a, s) =>
                K.LaunchIndexCallThenAssignedLengthKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.LengthThenCallKernel),
            static (o, a, s) => K.LaunchLengthThenCallKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.LengthThenAssignmentKernel),
            static (o, a, s) => K.LaunchLengthThenAssignmentKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.LengthThenIncrementKernel),
            static (o, a, s) => K.LaunchLengthThenIncrementKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.LengthArgumentThenCallKernel),
            static (o, a, s) => K.LaunchLengthArgumentThenCallKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.IndexLengthThenCompoundCallKernel),
            static (o, a, s) =>
                K.LaunchIndexLengthThenCompoundCallKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.IndexLengthThenAssignedCallKernel),
            static (o, a, s) =>
                K.LaunchIndexLengthThenAssignedCallKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.CheckedArgumentThenCallKernel),
            static (o, a, s) => K.LaunchCheckedArgumentThenCallKernel(o, a, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.NullElementThenAssignedCallKernel),
            static (o, a, s) =>
                K.LaunchNullElementThenAssignedCallKernel(o, a, K0, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithArray(nameof(K.DeconstructionElementThenCallKernel),
            static (o, a, s) =>
                K.LaunchDeconstructionElementThenCallKernel(o, a, K0, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithStructs(nameof(K.StructElementThenAssignedCallKernel),
            static (o, p, s) =>
                K.LaunchStructElementThenAssignedCallKernel(o, p, K0, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithStructs(nameof(K.StructReadIndexElementThenAssignedCallKernel),
            static (o, p, s) =>
                K.LaunchStructReadIndexElementThenAssignedCallKernel(o, p, K0, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithStructs(nameof(K.DeconstructionStructElementThenCallKernel),
            static (o, p, s) =>
                K.LaunchDeconstructionStructElementThenCallKernel(o, p, K0, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithoutNull(nameof(K.ElementThenAssignedCallKernel),
            static (o, s) => K.LaunchElementThenAssignedCallKernel(o, K0, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithoutNull(nameof(K.ReadIndexElementThenAssignedCallKernel),
            static (o, s) =>
                K.LaunchReadIndexElementThenAssignedCallKernel(o, K0, gridDimX: s.GridX, blockDimX: s.BlockX)),
        WithoutNull(nameof(K.ElementThenCompoundCallKernel),
            static (o, s) => K.LaunchElementThenCompoundCallKernel(o, K0, gridDimX: s.GridX, blockDimX: s.BlockX)),
    ];

    public static TheoryData<string> CaseNames => [.. s_cases.Select(static c => ShortName(c.Name))];

    [SkippableTheory]
    [MemberData(nameof(CaseNames))]
    public void ThrowOrder_WriteAndCheckInTwoOperands_KeepTheWritesOfCSharp(string caseName) =>
        DifferentialRunner.Run(s_cases.Single(c => ShortName(c.Name) == caseName));

    // ThrowOrderDifferentialKernels.CallThenLengthKernel -> CallThenLengthKernel
    private static string ShortName(string name) => name[(nameof(ThrowOrderDifferentialKernels).Length + 1)..];

    // a: null on seed 0, [7] on seed 1
    private static DifferentialCase WithArray(string kernel, Action<int[], int[]?, LaunchShape> launch) =>
        Create(kernel, [0, 1], (o, isNull, s) => launch(o, isNull ? null : [7], s));

    // s: null on seed 0, one pair on seed 1
    private static DifferentialCase WithStructs(string kernel, Action<int[], ThrowOrderPair[]?, LaunchShape> launch) =>
        Create(kernel, [0, 1], (o, isNull, s) => launch(o, isNull ? null : [new ThrowOrderPair()], s));

    // No null input: the form checks an element of o, in range (#718)
    private static DifferentialCase WithoutNull(string kernel, Action<int[], LaunchShape> launch) =>
        Create(kernel, [1], (o, _, s) => launch(o, s));

    // The case of one kernel: o starts as {-1, -1}, and seed 0 asks the launch for a null array
    private static DifferentialCase Create(string kernel, int[] seeds, Action<int[], bool, LaunchShape> launch) =>
        new(DiffKernelNames.Of(typeof(ThrowOrderDifferentialKernels), kernel))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = seeds,
            OracleExceptions = OracleExceptionMode.MatchOracle,
            Inputs = static (rng, _) => new DiffBuffers()
                .In("o", new[] { -1, -1 }, OutputPolicy.Exact)
                .In("isNull", new[] { rng.Seed == 0 ? 1 : 0 }, OutputPolicy.Exact, readOnly: true),
            Launch = (b, s) => launch(b.Get<int>("o"), b.Get<int>("isNull")[0] == 1, s),
            OracleIsLaunch = true,
        };
}
