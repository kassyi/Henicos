#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// The integer <c>Where</c> with its element count on the device (#633, design D3 4.4, test plan 6.5): everything after
/// <c>v.Where(m)</c> equals System.Linq's <c>Where</c> over the same values.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public sealed class WhereDeviceCountTests : IDisposable
{
    private const int Words = 3;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;
    private readonly List<IDisposable> _owned = [];

    public WhereDeviceCountTests()
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

    public static TheoryData<string, BigInteger[], BigInteger[], int> SelectionCases() => new()
    {
        // the #633 repro: Count 2, Min 3; a mask from a second vector; a signed predicate; none; all
        { "greater than 2", [3, 1, 5], [], 2 },
        { "mask", [-5, 3, 7, -2], [1, 0, 1, 1], 1 },
        { "negative", [-100, -3, 50], [], 0 },
        { "none", [1, 2, 3], [], 100 },
        { "all", [5, 6, 7], [], -100 },
    };

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(SelectionCases))]
    public void Consumers_AfterWhere_MatchSystemLinq(string name, BigInteger[] values, BigInteger[] mask, int threshold)
    {
        // P: Count / Sum / Max / Min / Any / All / Skip / Take and the public Evaluate after Where equal System.Linq.
        // "mask" selects where mask == 1; the others select v > threshold, except "negative": v < 0.
        // mutant: Where keeps the capacity as its count (the padding is counted)
        var selected = mask.Length > 0
            ? values.Where((_, i) => mask[i] == 1).ToArray()
            : name == "negative" ? values.Where(v => v < 0).ToArray() : values.Where(v => v > threshold).ToArray();
        var array = CreateArray(values);

        IGpuBigIntVectorExpression Filtered() => array.AsVectorizedEnumerable().Where(Predicate(array, values, mask, name, threshold));

        Assert.Equal(new BigInteger(selected.Length), ReadScalar(Filtered().Count()));
        Assert.Equal(selected.Aggregate(BigInteger.Zero, (s, v) => s + v), ReadScalar(Filtered().Sum()));
        if (selected.Length == 0)
        {
            Assert.Throws<InvalidOperationException>(() => Filtered().Max().Evaluate(_workspace));
            Assert.Throws<InvalidOperationException>(() => Filtered().Min().Evaluate(_workspace));
        }
        else
        {
            Assert.Equal(selected.Max(), ReadScalar(Filtered().Max()));
            Assert.Equal(selected.Min(), ReadScalar(Filtered().Min()));
        }

        using var zero = GpuBigUInt.FromBigInteger(_workspace, 0, Words);
        var scalarZero = new GpuBigInt(zero, false);
        using var any = Filtered().IsGreaterThan(scalarZero).Any();
        using var all = Filtered().IsGreaterThan(scalarZero).All();
        Assert.Equal(selected.Any(v => v > 0), any.Evaluate(_workspace));
        Assert.Equal(selected.All(v => v > 0), all.Evaluate(_workspace));

        Assert.Equal(new BigInteger(selected.Skip(1).Take(1).Count()), ReadScalar(Filtered().Skip(1).Take(1).Count()));

        using var filtered = Filtered();
        Assert.Throws<InvalidOperationException>(() => filtered.Length);
        Assert.Equal(values.Length, filtered.Capacity);
        Assert.Equal(selected, Read(filtered.Evaluate(_workspace)));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2047)]
    [InlineData(2048)]
    [InlineData(2049)]
    [InlineData(4097)]
    [InlineData(5000000)]
    public void LongVectors_CountAndSum(int length)
    {
        // One block (2047, 2048) and several (2049, 4097: the total comes from the last level of the block-sum scan;
        // 5 000 000: three levels). Every third element is selected.
        // mutant: the single-block scan without the total; the multi-block scan without passing the total down
        var values = Enumerable.Range(0, length).Select(i => new BigInteger(i)).ToArray();
        var flags = Enumerable.Range(0, length).Select(i => i % 3 == 0 ? 1u : 0u).ToArray();
        var array = CreateArray(values);
        var predicate = Flags(flags);
        var selected = values.Where((_, i) => i % 3 == 0).ToArray();

        Assert.Equal(new BigInteger(selected.Length), ReadScalar(array.AsVectorizedEnumerable().Where(predicate).Count()));
        Assert.Equal(selected.Aggregate(BigInteger.Zero, (s, v) => s + v),
            ReadScalar(array.AsVectorizedEnumerable().Where(Flags(flags)).Sum()));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NestedWhere_ReadsOnlyTheInnerCount()
    {
        // v.Where(p).Where(m): m has the capacity's length, and its flags after the inner count are 1, so a scan that
        // read past the inner count would count padding. The 8 flags are scanned by 4 threads, each reading positions
        // i and i + 4, so the flags past the count sit in both halves.
        // mutant: the scan without globalA < n (or globalB < n)
        BigInteger[] values = [-9, 4, -7, 8, 6, -1, 3, 2];
        uint[] inner = [0, 1, 0, 0, 0, 0, 1, 0]; // 4, 3: the inner count is 2
        uint[] outer = [1, 0, 1, 1, 1, 1, 1, 1]; // position 0 of the inner result: 4 (positions 2-7 are past the count)
        var array = CreateArray(values);
        var innerSelected = values.Where((_, i) => inner[i] == 1).ToArray();
        var expected = innerSelected.Where((_, i) => outer[i] == 1).ToArray();

        IGpuBigIntVectorExpression Nested() => array.AsVectorizedEnumerable().Where(Flags(inner)).Where(Flags(outer));

        Assert.Equal(new BigInteger(expected.Length), ReadScalar(Nested().Count()));
        Assert.Equal(expected.Max(), ReadScalar(Nested().Max()));
        Assert.Equal(expected.Min(), ReadScalar(Nested().Min()));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void PredicateValuesOtherThanOne_AreTrue()
    {
        // A flag vector from outside GpuMath may hold any non-zero value for true; the scan counts each as 1.
        // mutant: the scan without the 0/1 normalization (the positions shift)
        BigInteger[] values = [10, 20, 30, 40];
        var array = CreateArray(values);
        uint[] flags = [2, 0, 0xFFFFFFFF, 1];

        using var filtered = array.AsVectorizedEnumerable().Where(Flags(flags));

        Assert.Equal([new BigInteger(10), 30, 40], Read(filtered.Evaluate(_workspace)));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void PredicateOfAnotherLength_IsRejected()
    {
        // mutant: the capacity check removed (the scan reads past the predicate)
        var array = CreateArray([1, 2, 3]);
        using var filtered = array.AsVectorizedEnumerable().Where(Flags([1, 0]));

        Assert.Throws<ArgumentException>(() => filtered.Evaluate(_workspace));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Target_IsNotSupported()
    {
        var array = CreateArray([1, 2, 3]);
        using var filtered = array.AsVectorizedEnumerable().Where(Flags([1, 0, 1]));
        using var target = new GpuBigUIntArray(_workspace, 3, Words);

        Assert.Throws<NotSupportedException>(() => filtered.Evaluate(_workspace, targetResult: target));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CountInAGraph_IsCountedAgainOnEveryLaunch()
    {
        // The count of a Where is computed on the device, so a CUDA graph of Where(...).Count() counts again after the
        // input changed (the reason for keeping the count on the device, decision G4).
        // mutant: Count writes the capacity
        var array = CreateArray([-5, 3, -1, 8]);
        using var zero = GpuBigUInt.FromBigInteger(_workspace, 0, Words);
        var negative = array.AsVectorizedEnumerable().IsLessThan(new GpuBigInt(zero, false));
        var count = array.AsVectorizedEnumerable().Where(negative).Count();

        using var graph = count.CompileToGraph(_workspace);
        graph.Execute();
        var first = graph.Result.Value.ToBigInteger();
        Upload(array, [-5, -3, -1, -8]);
        graph.Execute();
        var second = graph.Result.Value.ToBigInteger();

        Assert.Equal((new BigInteger(2), new BigInteger(4)), (first, second));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void SumInAGraph_IsNotSupported()
    {
        // A reduction reads its result header back, which a capture cannot record: NotSupportedException with the
        // reason, instead of a CUDA error (design 4.8, until stage 7)
        var array = CreateArray([-5, 3, -1, 8]);
        var sum = array.AsVectorizedEnumerable().Where(Flags([1, 0, 1, 1])).Sum();

        Assert.Throws<NotSupportedException>(() => sum.CompileToGraph(_workspace));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void PublicEvaluate_InACapture_Throws()
    {
        // The public Evaluate of a filtered vector reads the count; inside a capture that is an InvalidOperationException
        var array = CreateArray([1, 2, 3]);
        using var filtered = array.AsVectorizedEnumerable().Where(Flags([1, 0, 1]));
        filtered.Evaluate(_workspace);

        var provider = _workspace.Provider;
        provider.BeginCapture(_workspace.Stream);
        string? error;
        try
        {
            error = EvaluateAndCatch(filtered);
        }
        finally
        {
            provider.DestroyGraph(provider.EndCapture(_workspace.Stream), IntPtr.Zero);
        }

        Assert.Equal(nameof(InvalidOperationException), error);
    }

    // A view cannot be captured by a lambda, so Assert.Throws cannot take the evaluation
    private string? EvaluateAndCatch(IGpuBigIntVectorExpression expression)
    {
        try
        {
            _ = expression.Evaluate(_workspace).Length;
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.GetType().Name;
        }
    }

    private IGpuBoolVectorExpression Predicate(GpuBigIntArray array, BigInteger[] values, BigInteger[] mask,
        string name, int threshold)
    {
        if (mask.Length > 0)
        {
            var maskArray = CreateArray(mask);
            return maskArray.AsVectorizedEnumerable().IsEqualTo(new GpuBigInt(Keep(GpuBigUInt.FromBigInteger(_workspace, 1, Words)), false));
        }

        var scalar = Keep(GpuBigUInt.FromBigInteger(_workspace, BigInteger.Abs(threshold), Words));
        var signed = new GpuBigInt(scalar, threshold < 0);
        return name == "negative"
            ? array.AsVectorizedEnumerable().IsLessThan(new GpuBigInt(Keep(GpuBigUInt.FromBigInteger(_workspace, 0, Words)), false))
            : array.AsVectorizedEnumerable().IsGreaterThan(signed);
    }

    private GpuBigUInt Keep(GpuBigUInt value)
    {
        _owned.Add(value);
        return value;
    }

    private IGpuBoolVectorExpression Flags(uint[] flags)
    {
        var array = new GpuBigUIntArray(_workspace, flags.Length, 1);
        _workspace.Provider.CopyHostToDevice(array.DevicePointer, flags, flags.Length, _workspace.Stream);
        _owned.Add(array);
        return new FlagsLeaf(array);
    }

    private BigInteger ReadScalar(GpuBigIntExpr expression)
    {
        using var result = expression.Evaluate(_workspace);
        return result.Value.ToBigInteger(result.IsNegative);
    }

    private GpuBigIntArray CreateArray(BigInteger[] values)
    {
        var array = new GpuBigIntArray(_workspace, values.Length, Words);
        _owned.Add(array);
        Upload(array, values);
        return array;
    }

    private void Upload(GpuBigIntArray array, BigInteger[] values)
    {
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
    }

    private BigInteger[] Read(GpuBigIntArrayView array)
    {
        var words = new uint[array.Length * Words];
        var signs = new uint[array.Length];
        if (array.Length > 0)
        {
            _workspace.Provider.CopyDeviceToHost(words, array.Values.DevicePointer, words.Length, _workspace.Stream);
            _workspace.Provider.CopyDeviceToHost(signs, array.Signs.DevicePointer, signs.Length, _workspace.Stream);
        }

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

    // A flag vector as a caller outside GpuMath would implement it: any non-zero value is true
    private sealed class FlagsLeaf(GpuBigUIntArray flags) : IGpuBoolVectorExpression
    {
        public int Length => flags.Length;

        public GpuBigUIntArrayView Evaluate(GpuWorkspace workspace) => flags;

        public void Dispose()
        {
            // The flags belong to the test
        }
    }
}
