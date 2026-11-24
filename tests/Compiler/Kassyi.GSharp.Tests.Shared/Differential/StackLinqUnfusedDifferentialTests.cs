#nullable enable
using System;
using System.Runtime.CompilerServices;

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

    // Rows of the boundary seed: the ends of int32 with elements that keep every sum in range (a Sum is exact and throws
    // when the exact sum does not fit, and the oracle must not throw), signs that select different elements
    private static readonly int[][] s_boundaryRows =
    [
        [1, 2, 3, 4],
        [int.MaxValue, 0, 0, -10],
        [int.MinValue, 0, 0, 10],
        [int.MaxValue, int.MinValue, 0, 0],
        [-10, -20, -30, -40],
        [0, -1, 1, 0],
        [5, -5, 5, -5],
        [-1, 7, -1, -1],
    ];

    internal static readonly DifferentialCase s_unfusedOperators = DiffCases.Create(
        StackLinqUnfusedDifferentialKernels.LaunchUnfusedOperatorKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSlices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, StackLinqUnfusedDifferentialKernels.UnfusedOperatorColumns, OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r");

    internal static readonly DifferentialCase s_unfusedDoubleSum = DiffCases.Create(
        StackLinqUnfusedDifferentialKernels.LaunchUnfusedDoubleSumKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSlices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r",
        reference: static (b, _) => UnfusedDoubleSumReference(b.Get<int>("xs"), b.Get<long>("r")));

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

    internal static readonly DifferentialCase s_indexedLambdas = DiffCases.Create(
        StackLinqUnfusedDifferentialKernels.LaunchIndexedLambdaKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSlices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, StackLinqUnfusedDifferentialKernels.IndexedLambdaColumns, OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r");

    // Seed 1 gives row 3 a threshold above every element: Last(predicate) finds no match and throws the message of
    // C# and System.Linq, on the fused and the unfused path alike
    internal static readonly DifferentialCase s_fusedLastNoMatch = LastPredicateCase(
        StackLinqUnfusedDifferentialKernels.LaunchLastPredicateNoMatchKernel);

    internal static readonly DifferentialCase s_unfusedLastNoMatch = LastPredicateCase(
        StackLinqUnfusedDifferentialKernels.LaunchUnfusedLastPredicateNoMatchKernel);

    private static DifferentialCase LastPredicateCase(
        Int32PairRowsLaunch launch,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: static (rng, shape) =>
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
            shape: LaunchShapes.Linear(TrapRows, 32),
            firstInputName: "xs",
            secondInputName: "thresholds",
            outputName: "dst",
            seeds: [0, 1],
            expectedException: static (rng, _) => rng.Seed == 1
                ? ExceptionPolicy.For<InvalidOperationException>(
                    DiffPaths.CheckedPaths(),
                    messageContains: "Sequence contains no matching element")
                : null,
            launchExpression: launchExpression);

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
        for (int i = 0; i < values.Length; i++)
        {
            // Keeps the sign and the varied bits, and no sum of the elements of a slice leaves the range of int32
            values[i] >>= 6;
        }

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
