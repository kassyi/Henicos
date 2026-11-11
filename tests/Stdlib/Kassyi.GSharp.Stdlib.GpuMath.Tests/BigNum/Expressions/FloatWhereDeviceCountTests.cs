#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// The float <c>Where</c> with its element count on the device
/// (#633 (Where keeps the source length, and empty integer Max returns 0)): Count, Sum, Max, Min, Average,
/// Skip / Take and the host loops after <c>Where</c> equal System.Linq over the same values.
/// </summary>
/// <remarks>
/// The inputs are small integers, so the sums and averages are exact in a 4-word significand and are compared exactly.
/// </remarks>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
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
        using var negative = array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsLessThan(Float(0)));

        Assert.Equal(2, negative.Count(_workspace));
        Assert.Equal(-4.0, ReadOwned(negative.Sum(_workspace)));
        Assert.Equal(-1.0, ReadOwned(negative.Max(_workspace)));
        Assert.Equal(-3.0, ReadOwned(negative.Min(_workspace)));
        Assert.Equal(-2.0, ReadOwned(negative.Average(_workspace)));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NoneSelected_IsEmpty()
    {
        // Nothing selected: Max / Min / Average throw as System.Linq, Sum is 0, Count is 0
        var array = CreateArray([1, 2, 3]);
        using var none = array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsLessThan(Float(0)));

        Assert.Equal(0, none.Count(_workspace));
        Assert.Equal(0.0, ReadOwned(none.Sum(_workspace)));
        Assert.Throws<InvalidOperationException>(() => none.Max(_workspace));
        Assert.Throws<InvalidOperationException>(() => none.Min(_workspace));
        Assert.Throws<InvalidOperationException>(() => none.Average(_workspace));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void HostLoop_RunsOnlyOverTheSelected()
    {
        // SelectLog runs on the host element by element; the padding after the selected elements is 0, whose logarithm
        // has no value, so the loop must stop at the count.
        // mutant: the host loop over the capacity
        var array = CreateArray([4, -1, 16, -5, 64]);

        using var logs = array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsGreaterThan(Float(0)))
            .SelectLog();

        Assert.Equal(Math.Log(64), ReadOwned(logs.Max(_workspace)), 8);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void HostLoop_InACapture_ThrowsInvalidOperation()
    {
        // SelectSqrt runs on the host element by element, so it reads the device count; inside a capture that is an
        // InvalidOperationException with the reason, not a CUDA error that invalidates the capture.
        // mutant: DeviceElementCount.Read without the capture check
        var array = CreateArray([4, -1, 16]);
        using var roots = array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsGreaterThan(Float(0)))
            .SelectSqrt();

        var provider = _workspace.Provider;
        provider.BeginCapture(_workspace.Stream);
        Exception? error;
        try
        {
            error = Record.Exception(() => roots.Count(_workspace));
        }
        finally
        {
            provider.DestroyGraph(provider.EndCapture(_workspace.Stream), IntPtr.Zero);
        }

        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(2, roots.Count(_workspace));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SevenOfThreeHundred_TwoPasses()
    {
        // 300 elements, 7 selected: the first pass has blocks without an element
        var values = Enumerable.Range(0, 300).Select(i => i % 43 == 0 ? 1000L + i : -i).ToArray();
        var selected = values.Where(v => v > 0).ToArray();
        var array = CreateArray(values);

        using var positive = array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsGreaterThan(Float(0)));

        Assert.Equal(selected.Length, positive.Count(_workspace));
        Assert.Equal((double)selected.Max(), ReadOwned(positive.Max(_workspace)));
        Assert.Equal((double)selected.Min(), ReadOwned(positive.Min(_workspace)));
        Assert.Equal((double)selected.Sum(), ReadOwned(positive.Sum(_workspace)));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SkipTake_AfterWhere()
    {
        var array = CreateArray([-3, 7, -1, 9, -8]);

        using var window = array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsLessThan(Float(0)))
            .Skip(1).Take(1);

        Assert.Equal(-1.0, ReadOwned(window.Max(_workspace)));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void LengthAndEvaluateTo_AfterWhere()
    {
        // Length is known only on the device (decision V1); EvaluateTo returns no count, so it refuses a filtered
        // vector and leaves the destination as it was
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

    private static readonly string[] s_scalarDivisions = ["SelectDivide", "SelectDivideByUInt32"];

    public static TheoryData<string> ScalarDivisions() => new(s_scalarDivisions);

    // Divisor 3 divides [6, 9] exactly; divisor 1 is the identity
    public static TheoryData<string, uint> NonZeroDivisions()
    {
        var data = new TheoryData<string, uint>();
        foreach (var operation in s_scalarDivisions)
        {
            data.Add(operation, 3);
            data.Add(operation, 1);
        }

        return data;
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ScalarDivisions))]
    public void ScalarDivision_ZeroDivisorAfterWhereSelectingNone_IsEmpty(string operation)
    {
        // P: System.Linq divides only the elements, so a zero divisor over a Where that selects nothing divides
        // nothing: Count 0 and Sum 0.
        // mutant: ZeroDivisorCheck.cs:28 `if (count.Read(workspace) > 0)` -> `if (count.Capacity > 0)`
        // input: SelectDivide
        // mutant: FloatVectorUnaryExpressions.cs:237 the `if (divisor == 0)` check moved back before the count is read
        // input: SelectDivideByUInt32
        var array = CreateArray([1, 2, 3]);

        IGpuBigFloatVectorExpression Divided() => DivideBy(
            array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsLessThan(Float(0))), operation, 0);

        using var forCount = Divided();
        using var forSum = Divided();
        Assert.Equal(0, forCount.Count(_workspace));
        Assert.Equal(0.0, ReadOwned(forSum.Sum(_workspace)));
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(ScalarDivisions))]
    public void ScalarDivision_ZeroDivisorAfterWhereSelectingSome_ThrowsDivideByZeroException(string operation)
    {
        // P: a zero divisor throws as soon as one element is divided, as System.Linq does (one of three selected).
        // mutant: ZeroDivisorCheck.cs:30 `throw new DivideByZeroException();` removed
        // input: SelectDivide
        // mutant: FloatVectorUnaryExpressions.cs:231 `if (count == 0)` -> `if (count <= 1)`
        // input: SelectDivideByUInt32
        // equivalent: removing the check at FloatVectorUnaryExpressions.cs:237 `if (divisor == 0)`; the element
        // division throws the same exception
        var array = CreateArray([-3, 1, 2]);

        using var divided = DivideBy(
            array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsLessThan(Float(0))), operation, 0);

        Assert.Throws<DivideByZeroException>(() => divided.Sum(_workspace));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ScalarDivisions))]
    public void ScalarDivision_ZeroDivisorOverAnEmptyVector_IsEmpty(string operation)
    {
        // P: a vector of capacity 0 has no element to divide: Count 0 and Sum 0, as System.Linq.
        // No mutant: this passed before the fix too (Count and Sum of a capacity-0 float vector do not reach the
        // division check); it fixes the result.
        var array = CreateArray([]);

        using var forCount = DivideBy(array.AsVectorizedEnumerable(), operation, 0);
        using var forSum = DivideBy(array.AsVectorizedEnumerable(), operation, 0);
        Assert.Equal(0, forCount.Count(_workspace));
        Assert.Equal(0.0, ReadOwned(forSum.Sum(_workspace)));
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(NonZeroDivisions))]
    public void ScalarDivision_NonZeroDivisorAfterWhere_DividesTheSelected(string operation, uint divisor)
    {
        // P: the zero divisor handling leaves the division by a non-zero divisor as it was: the sum of [6, 9] divided
        // by 3 or 1 (exact in the significand).
        // mutant: FloatVectorBinaryScalarExpressions.cs:66 `DispatchKernel(workspace, operands, (uint)elements);`
        // removed
        // input: SelectDivide
        // mutant: FloatVectorUnaryExpressions.cs:237 `if (divisor == 0)` -> `if (divisor == 1)`
        // input: SelectDivideByUInt32 by 1 (DivideByZeroException)
        var array = CreateArray([6, -3, 9]);

        using var divided = DivideBy(
            array.AsVectorizedEnumerable().Where(array.AsVectorizedEnumerable().IsGreaterThan(Float(0))), operation,
            divisor);

        Assert.Equal(new[] { 6.0, 9.0 }.Select(v => v / divisor).Sum(), ReadOwned(divided.Sum(_workspace)));
    }

    // The scalar division named by the theory data
    private IGpuBigFloatVectorExpression DivideBy(IGpuBigFloatVectorExpression source, string operation,
        uint divisor) =>
        operation switch
        {
            "SelectDivide" => source.SelectDivide(Float(divisor)),
            "SelectDivideByUInt32" => source.SelectDivideByUInt32(divisor),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

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

    // The value of a reduction result, which the caller owns
    private static double ReadOwned(GpuBigFloat value)
    {
        using (value)
        {
            return ToDouble(value);
        }
    }

    private static double ToDouble(GpuBigFloatView value)
    {
        var magnitude = (double)value.Significand.ToBigInteger() * Math.Pow(2.0, value.Exponent);
        return value.IsNegative ? -magnitude : magnitude;
    }
}
