#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of the StackLinq Average of big integers, against a BigInteger oracle computed from the host inputs:
// - the value is the sum divided by the count, truncated toward zero as C# integer division, for elements of
//   different widths (1, 2 and 33 words) and both signs;
// - the quotient has the width of the sum (the bound of the balanced tree of additions), whatever the count;
// - building and evaluating an Average leaves no device buffer behind once the result and the expression are disposed
//   (the count is a one-word temporary of the evaluation).

/// <summary>StackLinq Average on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class StackLinqAverageTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public StackLinqAverageTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    public static TheoryData<int> Counts() => AverageCases.Counts();

    [Theory]
    [MemberData(nameof(Counts))]
    [Scenario(TestScenario.Boundary)]
    public void SignedAverage_OfMixedWidths_IsTheTruncatedQuotient(int count) =>
        AverageCases.Signed(_workspace, count);

    [Theory]
    [MemberData(nameof(Counts))]
    [Scenario(TestScenario.Boundary)]
    public void UnsignedAverage_OfMixedWidths_IsTheTruncatedQuotient(int count) =>
        AverageCases.Unsigned(_workspace, count);

    // The count buffer outlived the expression (one live buffer per Average that nobody disposed)
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntStackLinqExtensions.cs Average -> the count
    // uploaded into a GpuBigUInt leaf of the first element's width
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void SignedAverage_LeavesNoBufferBehind()
    {
        var values = AverageCases.Values(9, 21);
        var elements = AverageCases.Leaves(_workspace, values);
        try
        {
            // Warm the pool with one evaluation, so that the measured one allocates nothing new that stays
            AverageCases.EvaluateAndDispose(_workspace, elements);
            var before = _workspace.GetMemoryStatistics();
            AverageCases.EvaluateAndDispose(_workspace, elements);
            var after = _workspace.GetMemoryStatistics();
            Assert.Equal(before.LiveBuffers, after.LiveBuffers);
        }
        finally
        {
            AverageCases.Dispose(elements);
        }
    }
}

/// <summary>StackLinq Average on the CPU fallback.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class StackLinqAverageCpuFallbackTests
{
    public static TheoryData<int> Counts() => AverageCases.Counts();

    [Theory]
    [MemberData(nameof(Counts))]
    [Scenario(TestScenario.Boundary)]
    public void Average_CpuFallbackWithVaryingCount_FollowsTheSameRules(int count)
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(1 << 16);
            AverageCases.Signed(workspace, count);
            AverageCases.Unsigned(workspace, count);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }
}

/// <summary>Inputs and oracle checks shared by the GPU and CPU fallback tests.</summary>
internal static class AverageCases
{
    private static readonly int[] s_widths = [1, 2, 33];

    // 1 (the count does not divide), 3 (not a power of two), 33 (the count needs more than five bits)
    public static TheoryData<int> Counts() => new() { 1, 3, 33 };

    // Values cycling through the widths, with alternating signs; their magnitudes fill their widths
    public static (BigInteger Value, int Words)[] Values(int count, int seed)
    {
        var random = new Random(seed);
        var values = new (BigInteger, int)[count];
        for (var i = 0; i < count; i++)
        {
            var words = s_widths[i % s_widths.Length];
            var bytes = new byte[words * 4 + 1];
            random.NextBytes(bytes);
            bytes[^1] = 0;
            var magnitude = new BigInteger(bytes);
            values[i] = (i % 2 == 0 ? magnitude : -magnitude, words);
        }

        return values;
    }

    public static GpuBigInt[] Leaves(GpuWorkspace workspace, (BigInteger Value, int Words)[] values)
    {
        var leaves = new GpuBigInt[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var limbs = new uint[values[i].Words];
            BigInteger.Abs(values[i].Value).WriteLimbs(limbs);
            leaves[i] = new GpuBigInt(new GpuBigUInt(workspace, limbs), values[i].Value.Sign < 0);
        }

        return leaves;
    }

    public static void Dispose(GpuBigInt[] leaves)
    {
        foreach (var leaf in leaves)
        {
            leaf.Dispose();
        }
    }

    public static void EvaluateAndDispose(GpuWorkspace workspace, GpuBigInt[] elements)
    {
        var average = elements.AsStackEnumerable().AsParallel().Average();
        using (average.Evaluate(workspace))
        {
        }

        average.Dispose();
    }

    public static void Signed(GpuWorkspace workspace, int count)
    {
        var values = Values(count, 1433 + count);
        var elements = Leaves(workspace, values);
        try
        {
            var sum = values.Aggregate(BigInteger.Zero, static (s, v) => s + v.Value);
            var average = elements.AsStackEnumerable().AsParallel().Average();
            var sumTree = elements.AsStackEnumerable().AsParallel().Sum();

            // The quotient keeps the width of the sum
            Assert.Equal(sumTree.Node.NumWords, average.Node.NumWords);
            using var result = average.Evaluate(workspace);
            var magnitude = result.Value.ToBigInteger();

            // BigInteger.Divide truncates toward zero, as C# integer division
            Assert.Equal(BigInteger.Divide(sum, count), result.IsNegative ? -magnitude : magnitude);
            Assert.Equal(sumTree.Node.NumWords, result.Value.NumWords);
            average.Dispose();
            sumTree.Dispose();

            // The generic Average of IGpuBigNumber (a one-word count for an exact integer type) gives the same value
            using var generic = GenericAverage(elements);
            var genericMagnitude = generic.Value.ToBigInteger();
            Assert.Equal(BigInteger.Divide(sum, count), generic.IsNegative ? -genericMagnitude : genericMagnitude);

            // The same values negated: the sum changes sign, and the quotient truncates toward zero
            var negated = Leaves(workspace, values.Select(static v => (-v.Value, v.Words)).ToArray());
            try
            {
                using var negatedAverage = GenericAverage(negated);
                var negatedMagnitude = negatedAverage.Value.ToBigInteger();
                Assert.Equal(BigInteger.Divide(-sum, count),
                    negatedAverage.IsNegative ? -negatedMagnitude : negatedMagnitude);
            }
            finally
            {
                Dispose(negated);
            }
        }
        finally
        {
            Dispose(elements);
        }
    }

    // The Average of the generic IGpuBigNumber extensions, which GpuBigInt's own Average otherwise hides
    private static T GenericAverage<T>(T[] values)
        where T : struct, IGpuBigNumber<T> =>
        values.AsStackEnumerable().AsParallel().Average();

        public static void Unsigned(GpuWorkspace workspace, int count)
    {
        var values = Values(count, 1350 + count);
        var elements = new GpuBigUInt[count];
        try
        {
            var sum = BigInteger.Zero;
            for (var i = 0; i < count; i++)
            {
                var limbs = new uint[values[i].Words];
                var magnitude = BigInteger.Abs(values[i].Value);
                magnitude.WriteLimbs(limbs);
                elements[i] = new GpuBigUInt(workspace, limbs);
                sum += magnitude;
            }

            // The generic Average of an exact integer type divides by a one-word count
            using var result = elements.AsStackEnumerable().AsParallel().Average();
            Assert.Equal(sum / count, result.ToBigInteger());
        }
        finally
        {
            foreach (var element in elements)
            {
                element.Dispose();
            }
        }
    }
}
