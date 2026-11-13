#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using F = Kassyi.GSharp.Tests.Shared.Differential.ThrowOrderForms;
using K = Kassyi.GSharp.Tests.Shared.Differential.ThrowOrderDifferentialKernels;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// A write to memory and a check that may throw in two operands of one expression happen in the C# order on every
/// path (#1786 (a throwing check in a later operand runs before an earlier call on MSVC)): the write of an earlier
/// operand is kept when a later operand throws, and a later operand writes nothing when an earlier one throws. The
/// oracle is the kernel's own C#; the outputs are compared after the exception too
/// (<see cref="DifferentialCase.ComparesOutputsAfterException"/>).
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Scenario(TestScenario.SadPath)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class ThrowOrderDifferentialTests
{
    internal static readonly DifferentialCase[] s_cases = [.. CreateCases()];

    public static TheoryData<string> CaseNames => [.. s_cases.Select(static c => ShortName(c.Name))];

    [SkippableTheory]
    [MemberData(nameof(CaseNames))]
    public void ThrowOrder_WriteAndCheckInTwoOperands_KeepTheWritesOfCSharp(string caseName) =>
        DifferentialRunner.Run(s_cases.Single(c => ShortName(c.Name) == caseName));

    // ThrowOrderDifferentialKernels.ThrowOrderKernel.CallThenLength.Null -> CallThenLength.Null
    private static string ShortName(string name) =>
        name[(nameof(ThrowOrderDifferentialKernels).Length + nameof(K.ThrowOrderKernel).Length + 2)..];

    private static IEnumerable<DifferentialCase> CreateCases()
    {
        for (int form = 0; form < F.Count; form++)
        {
            if (F.StoresS(form))
            {
                yield return Create(form, "Null", static () => [7], static () => null);
                yield return Create(form, "One", static () => [7], static () => [new ThrowOrderPair()]);
                continue;
            }

            if (F.ReadsA(form))
            {
                yield return Create(form, "Null", static () => null, static () => []);
            }

            yield return Create(form, "One", static () => [7], static () => []);
        }
    }

    private static DifferentialCase Create(int form, string value, Func<int[]?> createA,
        Func<ThrowOrderPair[]?> createS) =>
        new(DiffKernelNames.Of(typeof(ThrowOrderDifferentialKernels), nameof(K.ThrowOrderKernel),
            $"{FormName(form)}.{value}"))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            OracleExceptions = OracleExceptionMode.MatchOracle,
            ComparesOutputsAfterException = true,
            Inputs = static (_, _) => new DiffBuffers().In("o", new[] { -1, -1 }, OutputPolicy.Exact),
            Launch = (b, s) => K.LaunchThrowOrderKernel(
                b.Get<int>("o"), createA(), createS(), 0, form, gridDimX: s.GridX, blockDimX: s.BlockX),
            OracleIsLaunch = true,
        };

    private static string FormName(int form) => form switch
    {
        F.CallThenLength => nameof(F.CallThenLength),
        F.AssignmentThenLength => nameof(F.AssignmentThenLength),
        F.IncrementThenLength => nameof(F.IncrementThenLength),
        F.CallThenLengthArgument => nameof(F.CallThenLengthArgument),
        F.CallThenCheckedArgument => nameof(F.CallThenCheckedArgument),
        F.CallThenNotNull => nameof(F.CallThenNotNull),
        F.IndexCallThenCompoundLength => nameof(F.IndexCallThenCompoundLength),
        F.IndexCallThenAssignedLength => nameof(F.IndexCallThenAssignedLength),
        F.LengthThenCall => nameof(F.LengthThenCall),
        F.LengthThenAssignment => nameof(F.LengthThenAssignment),
        F.LengthThenIncrement => nameof(F.LengthThenIncrement),
        F.LengthArgumentThenCall => nameof(F.LengthArgumentThenCall),
        F.IndexLengthThenCompoundCall => nameof(F.IndexLengthThenCompoundCall),
        F.IndexLengthThenAssignedCall => nameof(F.IndexLengthThenAssignedCall),
        F.CheckedArgumentThenCall => nameof(F.CheckedArgumentThenCall),
        F.ElementThenAssignedCall => nameof(F.ElementThenAssignedCall),
        F.ReadIndexElementThenAssignedCall => nameof(F.ReadIndexElementThenAssignedCall),
        F.ElementThenCompoundCall => nameof(F.ElementThenCompoundCall),
        F.NullElementThenAssignedCall => nameof(F.NullElementThenAssignedCall),
        F.StructElementThenAssignedCall => nameof(F.StructElementThenAssignedCall),
        F.StructReadIndexElementThenAssignedCall => nameof(F.StructReadIndexElementThenAssignedCall),
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };
}
