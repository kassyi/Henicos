#nullable enable
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.ConstantStringIndexDifferentialKernels;

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
    internal static readonly DifferentialCase s_stringIndex = new(
        $"{nameof(ConstantStringIndexDifferentialKernels)}.{nameof(StringIndexKernel)}")
    {
        Shape = LaunchShapes.SingleThread(),
        // Seeds 0 to 9 read every element of Text; 10 reads one past its end and 11 a negative index
        Seeds = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, _) => new DiffBuffers()
            .In("index", IndexOf(rng.Seed), readOnly: true)
            .Out<int>("dst", Columns, OutputPolicy.Exact),
        Launch = static (b, s) => LaunchStringIndexKernel(b.Get<int>("index"), b.Get<int>("dst"),
            gridDimX: s.GridX, blockDimX: s.BlockX),
        Oracle = static (b, _) => StringIndexKernel(b.Get<int>("index"), b.Get<int>("dst")),
    };

    // index[0] into Text, index[1] into a three-element string
    private static int[] IndexOf(int seed) => seed switch
    {
        10 => [Text.Length, 0],
        11 => [-1, 0],
        _ => [seed, seed % 3],
    };

    [SkippableFact]
    public void Index_NonAsciiConstantString_ReadsTheUtf16CodeUnitOnEveryPath() =>
        DifferentialRunner.Run(s_stringIndex);
}
