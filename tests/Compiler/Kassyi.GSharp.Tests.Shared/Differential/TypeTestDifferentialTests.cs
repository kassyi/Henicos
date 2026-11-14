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

    internal static readonly DifferentialCase s_typeTest = new(
        nameof(TypeTestDifferentialKernels) + "." + nameof(TypeTestDifferentialKernels.TypeTestKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, TypeTestDifferentialKernels.Columns, OutputPolicy.Exact),
        Launch = static (b, s) => TypeTestDifferentialKernels.LaunchTypeTestKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_nullTypeTest = new(
        nameof(TypeTestDifferentialKernels) + "." + nameof(TypeTestDifferentialKernels.NullTypeTestKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, TypeTestDifferentialKernels.NullColumns, OutputPolicy.Exact),
        Launch = static (b, s) => TypeTestDifferentialKernels.LaunchNullTypeTestKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_patternScope = new(
        nameof(TypeTestDifferentialKernels) + "." + nameof(TypeTestDifferentialKernels.PatternScopeKernel))
    {
        Shape = LaunchShape.ForRows(Rows, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("inputs", DiffValues.Int32(rng, rows))
            .Out<int>("r", rows, TypeTestDifferentialKernels.ScopeColumns, OutputPolicy.Exact),
        Launch = static (b, s) => TypeTestDifferentialKernels.LaunchPatternScopeKernel(
            b.Get<int>("inputs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        // The oracle cannot run a barrier: the reference is the value that C# gives each row
        Reference = static (b, _) =>
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
        },
    };

    [SkippableFact]
    public void TypeTest_ValueOperand_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_typeTest);

    [SkippableFact]
    public void TypeTest_ArrayAndNullableOperands_MatchTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullTypeTest);

    [SkippableFact]
    public void PatternVariable_AcrossBarrierAndInSwitchSection_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_patternScope);
}
