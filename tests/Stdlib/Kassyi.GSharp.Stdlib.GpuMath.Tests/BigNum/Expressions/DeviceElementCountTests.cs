#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Consumers of a vector whose number of elements is known only on the device (design D3 4.1-4.3, test plan 6.4): the
/// storage holds the capacity C, a device word holds the count n, and every consumer looks at the first n elements only.
/// </summary>
/// <remarks>
/// <see cref="DeviceCountedLeafExpression"/> stands in for a <c>Where</c>. The C - n elements after the count hold loud
/// values (large positive and negative), so a consumer that reads them gives a visibly wrong result. The expected values
/// are System.Linq over the first n elements.
/// </remarks>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class DeviceElementCountTests : IDisposable
{
    private const int Words = 3;

    private static readonly BigInteger s_loud = BigInteger.One << 70;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;
    private readonly List<IDisposable> _owned = [];

    public DeviceElementCountTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        foreach (var owned in _owned)
        {
            owned.Dispose();
        }

        _workspace.Dispose();
        _scope.Dispose();
    }

    public enum Reduction
    {
        Sum,
        Max,
        Min
    }

    public static TheoryData<Reduction, int, int, bool> ReductionCases()
    {
        var data = new TheoryData<Reduction, int, int, bool>();
        foreach (var reduction in Enum.GetValues<Reduction>())
        {
            foreach (var (capacity, count) in new[] { (1000, 10), (1000, 0), (1000, 1000), (257, 256), (1, 1), (1, 0) })
            {
                data.Add(reduction, capacity, count, false);
            }

            // Positive values only: an empty block that wrote 0 would be the minimum
            data.Add(reduction, 1000, 10, true);
        }

        return data;
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ReductionCases))]
    public void Reduction_ReadsOnlyTheDeviceCount(Reduction reduction, int capacity, int count, bool positive)
    {
        // (1000, 10): blocks 1-3 of the first pass have no element. (1000, 0) and (1, 0): the device count is 0, which
        // the static empty check before the launch cannot see.
        // mutant: the pass batch size taken from the capacity instead of the device count (the loud tail is reduced)
        // mutant: an empty block writes 0 instead of nothing (Min over positive values becomes 0)
        var values = Values(count, positive);
        var leaf = Leaf(capacity, values);

        // GpuBigIntExpr is not disposable yet (D2 stage 6); the workspace frees what the nodes hold
        var expression = reduction switch
        {
            Reduction.Sum => leaf.Sum(),
            Reduction.Max => leaf.Max(),
            _ => leaf.Min()
        };

        if (count == 0 && reduction != Reduction.Sum)
        {
            Assert.Throws<InvalidOperationException>(() => expression.Evaluate(_workspace));
            return;
        }

        using var result = expression.Evaluate(_workspace);
        var expected = reduction switch
        {
            Reduction.Sum => values.Aggregate(BigInteger.Zero, (s, v) => s + v),
            Reduction.Max => values.Max(),
            _ => values.Min()
        };
        Assert.Equal(expected, result.Value.ToBigInteger(result.IsNegative));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(Reduction.Sum)]
    [InlineData(Reduction.Max)]
    [InlineData(Reduction.Min)]
    public void ZeroDeviceCount_WithDestination(Reduction reduction)
    {
        // n = 0 with a destination full of ones: Max / Min throw (the destination is unspecified), Sum writes +0 into
        // the destination and wraps it.
        var leaf = Leaf(1000, []);
        using var destination = GpuBigUInt.FromBigInteger(_workspace, (BigInteger.One << (Words * 32)) - 1, Words);
        GpuBigIntExpr expression = reduction switch
        {
            Reduction.Sum => leaf.Sum(),
            Reduction.Max => leaf.Max(),
            _ => leaf.Min()
        };

        if (reduction != Reduction.Sum)
        {
            Assert.Throws<InvalidOperationException>(() => expression.Evaluate(destination));
            return;
        }

        var result = expression.Evaluate(destination);
        Assert.Equal(destination.DevicePointer, result.Value.DevicePointer);
        Assert.Equal(BigInteger.Zero, result.Value.ToBigInteger(result.IsNegative));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1000, 10)]
    [InlineData(1000, 0)]
    [InlineData(1, 1)]
    public void Count_IsTheDeviceCount(int capacity, int count)
    {
        // mutant: Count writes the capacity
        var expression = Leaf(capacity, Values(count, false)).Count();
        using var result = expression.Evaluate(_workspace);
        Assert.Equal(new BigInteger(count), result.Value.ToBigInteger(result.IsNegative));
    }

    public static TheoryData<int, int> SkipTakeCases()
    {
        var data = new TheoryData<int, int>();
        int[] counts = [0, 1, 6, 7, 8, 30];
        foreach (var skip in counts)
        {
            foreach (var take in counts)
            {
                data.Add(skip, take);
            }
        }

        return data;
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SkipTakeCases))]
    public void SkipTake_ComposeOnTheDeviceCount(int skip, int take)
    {
        // (C, n) = (20, 7): Skip / Take compose on the descriptor (design 4.1), also past n and twice.
        // mutant: ResolveElementCount without the Skip; TakeFirst with Math.Max instead of Math.Min
        var values = Values(7, false);
        var expected = values.Skip(skip).Take(take).Skip(1).Take(take).ToArray();

        var count = Leaf(20, values).Skip(skip).Take(take).Skip(1).Take(take).Count();
        var sum = Leaf(20, values).Skip(skip).Take(take).Skip(1).Take(take).Sum();
        using var countResult = count.Evaluate(_workspace);
        using var sumResult = sum.Evaluate(_workspace);

        Assert.Equal(new BigInteger(expected.Length), countResult.Value.ToBigInteger());
        Assert.Equal(expected.Aggregate(BigInteger.Zero, (s, v) => s + v), sumResult.Value.ToBigInteger(sumResult.IsNegative));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(5, true)]
    [InlineData(5, false)]
    [InlineData(0, true)]
    public void AnyAll_LookOnlyAtTheDeviceCount(int count, bool elementsPositive)
    {
        // (16, n): the first n elements all > 0 (or all <= 0), the tail the opposite; Any / All of "> 0" see only the
        // first n. With n = 0: Any false, All true, as System.Linq.
        // mutant: the All kernel without i < n (the tail decides)
        var values = Enumerable.Range(1, count).Select(i => elementsPositive ? new BigInteger(i) : -i).ToArray();
        var tail = elementsPositive ? -s_loud : s_loud;
        var leaf = Leaf(16, values, tail);
        using var zero = GpuBigUInt.FromBigInteger(_workspace, 0, Words);
        var scalar = new GpuBigInt(zero, false);

        using var any = leaf.IsGreaterThan(scalar).Any();
        var anyResult = any.Evaluate(_workspace);
        using var all = Leaf(16, values, tail).IsGreaterThan(scalar).All();
        var allResult = all.Evaluate(_workspace);

        Assert.Equal(values.Any(v => v > 0), anyResult);
        Assert.Equal(values.All(v => v > 0), allResult);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Elementwise_PassesTheCountThrough()
    {
        // SelectAdd keeps the descriptor of its input, so the Sum after it reads n elements
        var values = Values(10, false);
        using var ten = GpuBigUInt.FromBigInteger(_workspace, 10, Words);

        var sum = Leaf(1000, values).SelectAdd(new GpuBigInt(ten, false)).Sum();
        using var result = sum.Evaluate(_workspace);

        Assert.Equal(values.Aggregate(BigInteger.Zero, (s, v) => s + v + 10), result.Value.ToBigInteger(result.IsNegative));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void HostLoop_RunsOnlyOverTheCount()
    {
        // SelectSqrt runs on the host element by element and throws on a negative element; the tail after the count is
        // negative, so the loop must stop at the count.
        // mutant: the host loop over the capacity (InvalidOperationException from the tail)
        var values = Enumerable.Range(1, 10).Select(i => new BigInteger(i * i)).ToArray();

        var sum = Leaf(1000, values, -s_loud).SelectSqrt().Sum();
        using var result = sum.Evaluate(_workspace);

        Assert.Equal(new BigInteger(55), result.Value.ToBigInteger(result.IsNegative));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Binary_OneSideDynamic_TakesItsCount()
    {
        // A static vector of length C and a dynamic one of capacity C: the result has the dynamic side's count
        var values = Values(10, false);
        var other = Enumerable.Range(0, 1000).Select(i => new BigInteger(i)).ToArray();
        using var staticArray = CreateArray(other);

        var sum = staticArray.AsVectorizedEnumerable().ZipAdd(Leaf(1000, values)).Sum();
        using var result = sum.Evaluate(_workspace);

        Assert.Equal(values.Select((v, i) => v + other[i]).Aggregate(BigInteger.Zero, (s, v) => s + v),
            result.Value.ToBigInteger(result.IsNegative));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Binary_BothSidesDynamic_IsNotSupported()
    {
        // Two counts that only the device can compare (design J4): NotSupportedException, not a silent choice
        var sum = Leaf(20, Values(7, false)).ZipAdd(Leaf(20, Values(5, false))).Sum();
        Assert.Throws<NotSupportedException>(() => sum.Evaluate(_workspace));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void PublicEvaluate_ReturnsExactlyTheCount()
    {
        var values = Values(7, false);
        using var leaf = Leaf(20, values);

        var result = leaf.Evaluate(_workspace);

        Assert.Equal(7, result.Length);
        Assert.Equal(values, Read(result));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Length_OfADynamicVector_Throws()
    {
        // Design V1 (J1 a): Length is known only on the device, so it throws; Capacity and TryGetLength answer
        using var leaf = Leaf(20, Values(7, false));
        using var selected = leaf.SelectAdd(new GpuBigInt(GpuBigUInt.FromBigInteger(_workspace, 1, Words), false));

        Assert.Throws<InvalidOperationException>(() => leaf.Length);
        Assert.Throws<InvalidOperationException>(() => selected.Length);
        Assert.Equal(20, selected.Capacity);
        Assert.False(selected.TryGetLength(out var length));
        Assert.Equal(0, length);
    }

    private IGpuBigIntVectorExpression Leaf(int capacity, BigInteger[] values, BigInteger? tail = null)
    {
        var storage = new BigInteger[capacity];
        for (var i = 0; i < capacity; i++)
        {
            storage[i] = i < values.Length ? values[i] : tail ?? (i % 2 == 0 ? s_loud : -s_loud);
        }

        var array = CreateArray(storage);
        var countWord = GpuBigUInt.FromBigInteger(_workspace, values.Length, 1);
        _owned.Add(array);
        _owned.Add(countWord);
        return new DeviceCountedLeafExpression(array, countWord);
    }

    // count values in [-3, 3], or in [1, 7] when positive
    private static BigInteger[] Values(int count, bool positive) =>
        Enumerable.Range(0, count).Select(i => new BigInteger(positive ? i % 7 + 1 : i % 7 - 3)).ToArray();

    private GpuBigIntArray CreateArray(BigInteger[] values)
    {
        var array = new GpuBigIntArray(_workspace, values.Length, Words);
        var words = new uint[values.Length * Words];
        var signs = new uint[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var magnitude = BigInteger.Abs(values[i]);
            for (var w = 0; w < Words; w++)
            {
                words[i * Words + w] = (uint)((magnitude >> (32 * w)) & uint.MaxValue);
            }

            signs[i] = values[i].Sign < 0 ? 1u : 0u;
        }

        if (values.Length > 0)
        {
            _workspace.Provider.CopyHostToDevice(array.Values.DevicePointer, words, words.Length, _workspace.Stream);
            _workspace.Provider.CopyHostToDevice(array.Signs.DevicePointer, signs, signs.Length, _workspace.Stream);
        }

        return array;
    }

    private BigInteger[] Read(GpuBigIntArrayView array)
    {
        var words = new uint[array.Length * Words];
        var signs = new uint[array.Length];
        _workspace.Provider.CopyDeviceToHost(words, array.Values.DevicePointer, words.Length, _workspace.Stream);
        _workspace.Provider.CopyDeviceToHost(signs, array.Signs.DevicePointer, signs.Length, _workspace.Stream);
        var result = new BigInteger[array.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var value = BigInteger.Zero;
            for (var w = Words - 1; w >= 0; w--)
            {
                value = (value << 32) | words[i * Words + w];
            }

            result[i] = signs[i] != 0 ? -value : value;
        }

        return result;
    }
}
