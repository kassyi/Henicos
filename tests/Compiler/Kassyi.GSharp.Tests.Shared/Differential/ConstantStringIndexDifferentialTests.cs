#nullable enable
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="ConstantStringIndexDifferentialKernels"/>: every element of a constant string is
/// the UTF-16 code unit C# reads on every path, and a run-time index outside the string throws
/// <see cref="System.IndexOutOfRangeException"/>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class ConstantStringIndexDifferentialTests
{
    internal static readonly DifferentialCase s_stringIndex = DiffCases.Create(
        ConstantStringIndexDifferentialKernels.LaunchStringIndexKernel,
        inputs: static (rng, _) => new DiffBuffers()
            .In("index", IndexOf(rng.Seed), readOnly: true)
            .Out<int>("dst", ConstantStringIndexDifferentialKernels.Columns, OutputPolicy.Exact),
        shape: LaunchShapes.SingleThread(),
        inputName: "index",
        // Seeds 0 to 9 read every element of Text; 10 reads one past its end and 11 a negative index
        seeds: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    internal static readonly DifferentialCase s_constantIndexOutside = DiffCases.Create(
        ConstantStringIndexDifferentialKernels.LaunchConstantIndexOutsideKernel,
        inputs: static (rng, _) => new DiffBuffers()
            .In("mode", new[] { rng.Seed }, readOnly: true)
            .Out<int>("dst", 3, OutputPolicy.Exact),
        shape: LaunchShapes.SingleThread(),
        inputName: "mode",
        // 0: only the branch that never runs indexes outside; 1: the constant index outside runs
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    internal static readonly DifferentialCase s_forEach = DiffCases.Create(
        ConstantStringIndexDifferentialKernels.LaunchForEachKernel,
        inputs: static (_, _) => new DiffBuffers()
            .Out<int>("dst", ConstantStringIndexDifferentialKernels.ForEachColumns, OutputPolicy.Exact),
        shape: LaunchShapes.SingleThread(),
        bufferName: "dst",
        seeds: [0]);

    // index[0] into Text, index[1] into a three-element string
    private static int[] IndexOf(int seed) => seed switch
    {
        10 => [ConstantStringIndexDifferentialKernels.Text.Length, 0],
        11 => [-1, 0],
        _ => [seed, seed % 3],
    };

    [SkippableFact]
    public void Index_NonAsciiConstantString_ReadsTheUtf16CodeUnitOnEveryPath() =>
        DifferentialRunner.Run(s_stringIndex);

    [SkippableFact]
    public void Index_ConstantOutsideTheString_ThrowsOnlyWhereItRuns() =>
        DifferentialRunner.Run(s_constantIndexOutside);

    [SkippableFact]
    public void ForEach_ConstantString_ReadsEachUtf16CodeUnitOnEveryPath() =>
        DifferentialRunner.Run(s_forEach);

    // Text has 11 code units; the literals add 2 and 2, then dst[15] = 115 and dst[16] stays 0
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ForEach_ConstantString_WritesTheCodeUnitsAndNoTerminatingZero()
    {
        var dst = new int[ConstantStringIndexDifferentialKernels.ForEachColumns];

        ConstantStringIndexDifferentialKernels.LaunchForEachKernel(dst);

        Assert.Equal([97, 233, 0xd83d, 0xde00, 0, 34, 92, 63, 233, 102, 57, 120, 233, 0xe9, 0x41, 115, 0], dst);
    }
}
