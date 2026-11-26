#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential test for <c>is</c> on a value: C# decides the test by the type of the value (an identity, a nullable of
/// it, a type it boxes to), and still evaluates the operand (#642).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.HappyPath)]
public class TypeTestDifferentialTests
{
    private const int Rows = 128;

    internal static readonly DifferentialCase s_typeTest = DiffCases.Create(
        TypeTestDifferentialKernels.LaunchTypeTestKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, TypeTestDifferentialKernels.Columns, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "inputs",
        outputName: "r");

    internal static readonly DifferentialCase s_nullTypeTest = DiffCases.Create(
        TypeTestDifferentialKernels.LaunchNullTypeTestKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, TypeTestDifferentialKernels.NullColumns, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "inputs",
        outputName: "r");

    internal static readonly DifferentialCase s_patternScope = DiffCases.Create(
        TypeTestDifferentialKernels.LaunchPatternScopeKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, TypeTestDifferentialKernels.ScopeColumns, OutputPolicy.Exact),
        shape: LaunchShape.ForRows(Rows, blockX: 64),
        inputName: "inputs",
        outputName: "r",
        // The reference is the value that C# gives each row
        reference: static (b, _) =>
        {
            var inputs = b.Get<int>("inputs");
            var r = b.Get<int>("r");
            for (int tid = 0; tid < inputs.Length; tid++)
            {
                int v = inputs[tid] % 1000;
                int? maybe = v % 2 == 0 ? v : null;
                r[tid * 2] = (maybe ?? -1) + 1;
                r[(tid * 2) + 1] = (v % 3) switch
                {
                    0 => maybe ?? -1,
                    1 => maybe is { } one ? one + 10 : -2,
                    _ => -3
                };
            }
        });

    [SkippableFact]
    public void TypeTest_ValueOperand_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_typeTest);

    [SkippableFact]
    public void TypeTest_ArrayAndNullableOperands_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullTypeTest);

    [SkippableFact]
    public void PatternVariable_IsNotAssignedAndInSwitchSection_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_patternScope);
}
