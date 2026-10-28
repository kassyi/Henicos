#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// The float <c>Where</c> with its element count on the device (#633, design D3 test plan 6.6): Count, Sum, Max, Min,
/// Average, Skip / Take and the host loops after <c>Where</c> equal System.Linq over the same values.
/// </summary>
/// <remarks>
/// The inputs are small integers, so the sums and averages are exact in a 4-word significand and are compared exactly.
/// </remarks>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class FloatWhereDeviceCountTests : IDisposable
{
    private const int Words = 4;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;
    private readonly List<IDisposable> _owned = [];

    public FloatWhereDeviceCountTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
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

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Repro_NegativeOfThree()
    {
        // The #633 repro: [-3, -1, 5].Where(x < 0): Count 2, Max -1, Average -2 (they were 3, 0 and -4/3 with the
        // zero padding)
        // mutant: the float Where keeps the capacity as its count; Average divides by the capacity
        var array = CreateArray([-3, -1, 5]);

        IGpuBigFloatVectorExpression Negative() => array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsLessThan(Float(0)));

        Assert.Equal(2, Negative().Count(_workspace));
        Assert.Equal(-4.0, ToDouble(Negative().Sum(_workspace)));
        Assert.Equal(-1.0, ToDouble(Negative().Max(_workspace)));
        Assert.Equal(-3.0, ToDouble(Negative().Min(_workspace)));
        Assert.Equal(-2.0, ToDouble(Negative().Average(_workspace)));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NoneSelected_IsEmpty()
    {
        // Nothing selected: Max / Min / Average throw as System.Linq, Sum is 0, Count is 0
        var array = CreateArray([1, 2, 3]);

        IGpuBigFloatVectorExpression None() => array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsLessThan(Float(0)));

        Assert.Equal(0, None().Count(_workspace));
        Assert.Equal(0.0, ToDouble(None().Sum(_workspace)));
        Assert.Throws<InvalidOperationException>(() => None().Max(_workspace));
        Assert.Throws<InvalidOperationException>(() => None().Min(_workspace));
        Assert.Throws<InvalidOperationException>(() => None().Average(_workspace));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void HostLoop_RunsOnlyOverTheSelected()
    {
        // SelectLog runs on the host element by element; the padding after the selected elements is 0, whose logarithm
        // has no value, so the loop must stop at the count.
        // mutant: the host loop over the capacity
        var array = CreateArray([4, -1, 16, -5, 64]);

        var logs = array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsGreaterThan(Float(0))).SelectLog();
        using var max = logs.Max(_workspace);

        Assert.Equal(Math.Log(64), ToDouble(max), 8);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SevenOfThreeHundred_TwoPasses()
    {
        // 300 elements, 7 selected: the first pass has blocks without an element
        var values = Enumerable.Range(0, 300).Select(i => i % 43 == 0 ? 1000L + i : -i).ToArray();
        var selected = values.Where(v => v > 0).ToArray();
        var array = CreateArray(values);

        IGpuBigFloatVectorExpression Positive() => array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsGreaterThan(Float(0)));

        Assert.Equal(selected.Length, Positive().Count(_workspace));
        Assert.Equal((double)selected.Max(), ToDouble(Positive().Max(_workspace)));
        Assert.Equal((double)selected.Min(), ToDouble(Positive().Min(_workspace)));
        Assert.Equal((double)selected.Sum(), ToDouble(Positive().Sum(_workspace)));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SkipTake_AfterWhere()
    {
        var array = CreateArray([-3, 7, -1, 9, -8]);

        using var max = array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsLessThan(Float(0)))
            .Skip(1).Take(1).Max(_workspace);

        Assert.Equal(-1.0, ToDouble(max));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void LengthAndEvaluateTo_AfterWhere()
    {
        // Length is known only on the device (decision V1); EvaluateTo returns no count, so it refuses a filtered
        // vector and leaves the destination as it was (design 4.1.3)
        var array = CreateArray([-3, -1, 5]);
        using var filtered = array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsLessThan(Float(0)));
        using var destination = new GpuBigFloatArray(_workspace, 3, Words);
        destination.SetElement(0, Float(42));

        Assert.Throws<InvalidOperationException>(() => filtered.Length);
        Assert.Equal(3, filtered.Capacity);
        Assert.Throws<InvalidOperationException>(() => filtered.EvaluateTo(_workspace, destination));
        Assert.Equal(42.0, ToDouble(destination.GetElement(0)));

        var result = filtered.Evaluate(_workspace);
        Assert.Equal(2, result.Length);
        Assert.Equal(-3.0, ToDouble(result.GetElement(0)));
        Assert.Equal(-1.0, ToDouble(result.GetElement(1)));
    }

    private GpuBigFloat Float(long value)
    {
        var magnitude = (ulong)Math.Abs(value);
        var words = new uint[Words];
        words[0] = (uint)magnitude;
        words[1] = (uint)(magnitude >> 32);
        var significand = new GpuBigUInt(_workspace, words);
        var result = new GpuBigFloat(significand, 0, value < 0);
        _owned.Add(result);
        return result;
    }

    private GpuBigFloatArray CreateArray(long[] values)
    {
        var array = new GpuBigFloatArray(_workspace, values.Length, Words);
        _owned.Add(array);
        for (var i = 0; i < values.Length; i++)
        {
            array.SetElement(i, Float(values[i]));
        }

        return array;
    }

    private static double ToDouble(GpuBigFloatView value)
    {
        var magnitude = (double)value.Significand.ToBigInteger() * Math.Pow(2.0, value.Exponent);
        return value.IsNegative ? -magnitude : magnitude;
    }
}
