#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for the StackLinq calls that are not fused into one loop (<see cref="StackLinqUnfusedDifferentialKernels"/>):
/// every path must build and give the values and exceptions of C# StackLinq.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class StackLinqUnfusedDifferentialTests
{
    private const int SliceLen = StackLinqUnfusedDifferentialKernels.SliceLength;
    private const int TrapRows = 32;

    // Rows of the boundary seed: wrapping sums, a single repeated value, signs that select different elements
    private static readonly int[][] s_boundaryRows =
    [
        [1, 2, 3, 4],
        [int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue],
        [int.MinValue, int.MinValue, int.MinValue, int.MinValue],
        [int.MaxValue, int.MinValue, int.MaxValue, -1],
        [-10, -20, -30, -40],
        [0, -1, 1, 0],
        [5, -5, 5, -5],
        [-1, 7, -1, -1],
    ];

    internal static readonly DifferentialCase s_unfusedOperators = new(
        $"{nameof(StackLinqUnfusedDifferentialKernels)}.{nameof(StackLinqUnfusedDifferentialKernels.UnfusedOperatorKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSlices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, StackLinqUnfusedDifferentialKernels.UnfusedOperatorColumns, OutputPolicy.Exact),
        Launch = static (b, s) => StackLinqUnfusedDifferentialKernels.LaunchUnfusedOperatorKernel(
            b.Get<int>("xs"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_unfusedDoubleSum = new(DiffKernelNames.Of(
        typeof(StackLinqUnfusedDifferentialKernels),
        nameof(StackLinqUnfusedDifferentialKernels.UnfusedDoubleSumKernel)
    ))
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSlices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, OutputPolicy.Exact),
        Launch = static (b, s) => StackLinqUnfusedDifferentialKernels.LaunchUnfusedDoubleSumKernel(
            b.Get<int>("xs"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Reference = static (b, _) => UnfusedDoubleSumReference(b.Get<int>("xs"), b.Get<long>("r")),
    };

    // A floating-point Sum stops the C# run of the launcher (it cannot tell that a sum of four ints does not depend on
    // the order), so the reference sums each slice in order, as the generated code does; four ints sum exactly
    private static void UnfusedDoubleSumReference(int[] xs, long[] r)
    {
        for (int t = 0; (t + 1) * SliceLen <= xs.Length; t++)
        {
            double sum = 0;
            for (int j = 0; j < SliceLen; j++)
            {
                sum += xs[(t * SliceLen) + j];
            }

            r[t] = (long)sum;
        }
    }

    internal static readonly DifferentialCase s_indexedLambdas = new(
        $"{nameof(StackLinqUnfusedDifferentialKernels)}.{nameof(StackLinqUnfusedDifferentialKernels.IndexedLambdaKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSlices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, StackLinqUnfusedDifferentialKernels.IndexedLambdaColumns, OutputPolicy.Exact),
        Launch = static (b, s) => StackLinqUnfusedDifferentialKernels.LaunchIndexedLambdaKernel(
            b.Get<int>("xs"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        OracleIsLaunch = true,
    };

    // Seed 1 gives row 3 a threshold above every element: Last(predicate) finds no match and throws the message of
    // C# and System.Linq, on the fused and the unfused path alike
    internal static readonly DifferentialCase s_fusedLastNoMatch = LastPredicateCase(
        nameof(StackLinqUnfusedDifferentialKernels.LastPredicateNoMatchKernel),
        static (x, t, d, s) => StackLinqUnfusedDifferentialKernels.LaunchLastPredicateNoMatchKernel(
            x, t, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    internal static readonly DifferentialCase s_unfusedLastNoMatch = LastPredicateCase(
        nameof(StackLinqUnfusedDifferentialKernels.UnfusedLastPredicateNoMatchKernel),
        static (x, t, d, s) => StackLinqUnfusedDifferentialKernels.LaunchUnfusedLastPredicateNoMatchKernel(
            x, t, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX));

    private delegate void LastLaunch(int[] xs, int[] thresholds, int[] dst, LaunchShape shape);

    private static DifferentialCase LastPredicateCase(string kernel, LastLaunch launch) =>
        new($"{nameof(StackLinqUnfusedDifferentialKernels)}.{kernel}")
        {
            Shape = LaunchShapes.Linear(TrapRows, 32),
            Seeds = [0, 1],
            ExpectedException = static (rng, _) => rng.Seed == 1
                ? ExceptionPolicy.For<InvalidOperationException>(
                    DiffPaths.CheckedPaths(),
                    messageContains: "Sequence contains no matching element")
                : null,
            Inputs = static (rng, shape) =>
            {
                int rows = shape.Rows;
                var xs = new int[rows * SliceLen];
                var thresholds = new int[rows];
                for (int k = 0; k < rows; k++)
                {
                    for (int j = 0; j < SliceLen; j++)
                    {
                        xs[(k * SliceLen) + j] = (k * 10) + j + 1;
                    }
                }

                if (rng.Seed == 1)
                {
                    thresholds[3] = int.MaxValue;
                }

                return new DiffBuffers()
                    .In("xs", xs, readOnly: true)
                    .In("thresholds", thresholds, readOnly: true)
                    .Out<int>("dst", rows, StackLinqUnfusedDifferentialKernels.LastPredicateColumns, OutputPolicy.Exact);
            },
            Launch = (b, s) => launch(b.Get<int>("xs"), b.Get<int>("thresholds"), b.Get<int>("dst"), s),
            OracleIsLaunch = true,
        };

    [SkippableFact]
    public void StackLinq_UnfusedOperators_MatchTheOracleOnEveryPath() => DifferentialRunner.Run(s_unfusedOperators);

    [SkippableFact]
    public void StackLinq_UnfusedDoubleSum_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_unfusedDoubleSum);

    [SkippableFact]
    public void StackLinq_IndexedLambdas_MatchTheOracleOnEveryPath() => DifferentialRunner.Run(s_indexedLambdas);

    [SkippableFact]
    public void StackLinq_FusedLastPredicateWithoutMatch_ThrowsNoMatchingElementOnEveryPath() =>
        DifferentialRunner.Run(s_fusedLastNoMatch);

    [SkippableFact]
    public void StackLinq_UnfusedLastPredicateWithoutMatch_ThrowsNoMatchingElementOnEveryPath() =>
        DifferentialRunner.Run(s_unfusedLastNoMatch);

    private static int[] BuildSlices(DiffRandom rng, int rows)
    {
        var values = DiffValues.Int32(rng, rows * SliceLen);
        if (rng.IsBoundarySeed)
        {
            for (int r = 0; r < s_boundaryRows.Length && r < rows; r++)
            {
                Array.Copy(s_boundaryRows[r], 0, values, r * SliceLen, SliceLen);
            }
        }

        return values;
    }
}
