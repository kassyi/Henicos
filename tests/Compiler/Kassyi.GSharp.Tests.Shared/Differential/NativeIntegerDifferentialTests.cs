#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>Differential tests of <c>nint</c> and <c>nuint</c> in device code.</summary>
public class NativeIntegerDifferentialTests
{
    private const int Rows = 64;

    internal static readonly DifferentialCase s_constants = new(
        $"{nameof(NativeIntegerDifferentialKernels)}." +
        $"{nameof(NativeIntegerDifferentialKernels.NativeIntegerConstantKernel)}")
    {
        Shape = new LaunchShape(1, 1, 1, 1, 1, 1, 1),
        Inputs = static (rng, rows) => new DiffBuffers()
            .Out<long>(
                "dst", 6,
                [
                    "shifted_constant", "cast_of_int", "unsigned_shift", "unsigned_wrap", "unsigned_product",
                    "max_shifted"
                ],
                OutputPolicy.Exact),
        Launch = static (b, s) => NativeIntegerDifferentialKernels.LaunchNativeIntegerConstantKernel(
            b.Get<long>("dst"), 123456, gridDimX: s.GridX, blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_ConstantsAndCasts_MatchTheOracleOnEveryPath() => DifferentialRunner.Run(s_constants);

    private static long[] SortRows(DiffRandom rng, int rows)
    {
        // Rows of four with negatives, duplicates and both ends; the first rows are fixed
        long[] pool = [long.MinValue, long.MaxValue, -1, 0, 1, -5, 5, 5, 1L << 32, -(1L << 32), 0x8000_0000L];
        long[] fixedRows =
        [
            long.MaxValue, long.MinValue, 0, -1,
            5, 5, 5, 5,
            -3, long.MinValue, -3, long.MaxValue,
            1, 0, -1, 1L << 32,
        ];
        var values = new long[rows * 4];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = i < fixedRows.Length ? fixedRows[i] : pool[rng.NextBelow(pool.Length)];
        }

        return values;
    }

    private static DifferentialCase SortCase(
        string kernel, bool unsigned, Action<long[], long[], LaunchShape> launch) =>
        new($"{nameof(NativeIntegerDifferentialKernels)}.{kernel}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(Rows),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("a", SortRows(rng, rows.Rows))
                .Out<long>("dst", rows.Rows, ["k0", "k1", "k2", "k3"], OutputPolicy.Exact),
            Launch = (b, s) => launch(b.Get<long>("a"), b.Get<long>("dst"), s),
            // The host sort of each row of four is the result
            Reference = (b, s) =>
            {
                var a = b.Get<long>("a");
                var dst = b.Get<long>("dst");
                for (int row = 0; row < s.Rows; row++)
                {
                    var keys = a.AsSpan(row * 4, 4).ToArray();
                    if (unsigned)
                    {
                        var bits = Array.ConvertAll(keys, static k => (ulong)k);
                        Array.Sort(bits);
                        keys = Array.ConvertAll(bits, static k => (long)k);
                    }
                    else
                    {
                        Array.Sort(keys);
                    }

                    keys.CopyTo(dst, row * 4);
                }
            },
        };

    internal static readonly DifferentialCase s_sortNint = SortCase(
        nameof(NativeIntegerDifferentialKernels.NativeIntegerSortKernel), unsigned: false,
        static (a, dst, s) => NativeIntegerDifferentialKernels.LaunchNativeIntegerSortKernel(
            a, dst, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_sortNuint = SortCase(
        nameof(NativeIntegerDifferentialKernels.NativeUnsignedIntegerSortKernel), unsigned: true,
        static (a, dst, s) => NativeIntegerDifferentialKernels.LaunchNativeUnsignedIntegerSortKernel(
            a, dst, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    private static DifferentialCase ParameterCase(nint p, nuint q) =>
        new($"{nameof(NativeIntegerDifferentialKernels)}." +
            $"{nameof(NativeIntegerDifferentialKernels.NativeIntegerParameterKernel)}")
        {
            Shape = new LaunchShape(1, 1, 1, 1, 1, 1, 1),
            Inputs = static (rng, rows) => new DiffBuffers()
                .Out<long>(
                    "dst", 6,
                    ["nint", "nuint", "nint_plus_one", "nuint_half", "nuint_doubled", "nint_negative"],
                    OutputPolicy.Exact),
            Launch = (b, s) => NativeIntegerDifferentialKernels.LaunchNativeIntegerParameterKernel(
                b.Get<long>("dst"), p, q, gridDimX: s.GridX, blockDimX: s.BlockX),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_negativeParameter =
        ParameterCase((nint)(-0x1_0000_0005L), (nuint)0x1_0000_0001UL);
    internal static readonly DifferentialCase s_extremeParameter = ParameterCase(nint.MinValue, nuint.MaxValue);
    internal static readonly DifferentialCase s_largeParameter =
        ParameterCase(nint.MaxValue, (nuint)0x8000_0000_0000_0000UL);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_SpanSortOfNint_MatchesTheHostSortOnEveryPath() => DifferentialRunner.Run(s_sortNint);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_SpanSortOfNuint_MatchesTheHostSortOnEveryPath() => DifferentialRunner.Run(s_sortNuint);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_ParameterNegativeAndAboveUInt32_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_negativeParameter);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_ParameterAtTheExtremes_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_extremeParameter);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void NativeInteger_ParameterMaximumAndHighBit_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_largeParameter);
}
