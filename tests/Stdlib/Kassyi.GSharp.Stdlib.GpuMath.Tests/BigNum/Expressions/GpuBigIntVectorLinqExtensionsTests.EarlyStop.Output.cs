#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Any and All stop at the deciding element (see EarlyStop.Int.cs). This file: under a recording scope, a node sets its
// output from the recorded element on to zero, so that later host operations do not run on what the buffer held. The
// answer of Any / All cannot show it, so these tests evaluate the node into a target filled with ones and read it.
public partial class GpuBigIntVectorLinqExtensionsTests
{
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void SelectSqrt_RecordedUnderScope_ZerosFromRecordedElement()
    {
        using var arr = CreateArray([4, 9, -16, 25]);
        using var expression = arr.AsVectorizedEnumerable().SelectSqrt();

        // mutant: VectorSqrtExpression without RecordedTail.Clear (elements 2 and 3 keep the ones)
        var elements = EvaluateIntUnderScope(expression, 4, expectedIndex: 2);

        Assert.Equal([new BigInteger(2), 3, 0, 0], elements);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void SelectDivideByUInt32_ZeroDivisorUnderScope_ZerosOutput()
    {
        using var arr = CreateArray([4, 9]);
        using var expression = arr.AsVectorizedEnumerable().SelectDivideByUInt32(0u);

        // mutant: VectorDivByUInt32Expression without RecordedTail.Clear
        var elements = EvaluateIntUnderScope(expression, 2, expectedIndex: 0);

        Assert.Equal([BigInteger.Zero, 0], elements);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void SelectDivide_ZeroScalarUnderScope_ZerosOutput()
    {
        using var arr = CreateArray([4, 9]);
        using var zero = CreateScalar(0);
        using var expression = arr.AsVectorizedEnumerable().SelectDivide(zero);

        // mutant: VectorBinaryScalarExpressionBase.SkipsZeroDivision without RecordedTail.Clear
        var elements = EvaluateIntUnderScope(expression, 2, expectedIndex: 0);

        Assert.Equal([BigInteger.Zero, 0], elements);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void FloatSelectLog_RecordedUnderScope_ZerosFromRecordedElement()
    {
        using var arr = CreateFloatArray([4, -4, 16]);
        using var expression = arr.AsVectorizedEnumerable().SelectLog();

        // mutant: FloatVectorElementwiseHelper.ExecuteUnary without RecordedTail.Clear
        var elements = EvaluateFloatUnderScope(expression, 3, expectedIndex: 1);

        Assert.False(IsFloatZero(elements[0]));
        Assert.True(IsFloatZero(elements[1]));
        Assert.True(IsFloatZero(elements[2]));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void FloatZipPow_RecordedUnderScope_ZerosFromRecordedElement()
    {
        using var bases = CreateFloatArray([4, -2, 9]);
        using var exponents = CreateFloatArray([2, 2, 2]);
        using var expression = bases.AsVectorizedEnumerable().ZipPow(exponents.AsVectorizedEnumerable());

        // mutant: FloatVectorElementwiseHelper.ExecuteBinary without RecordedTail.Clear
        var elements = EvaluateFloatUnderScope(expression, 3, expectedIndex: 1);

        Assert.False(IsFloatZero(elements[0]));
        Assert.True(IsFloatZero(elements[1]));
        Assert.True(IsFloatZero(elements[2]));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void FloatSelectDivideByUInt32_ZeroDivisorUnderScope_ZerosOutput()
    {
        using var arr = CreateFloatArray([1, 2]);
        using var expression = arr.AsVectorizedEnumerable().SelectDivideByUInt32(0u);

        // mutant: FloatVectorDivByUInt32Expression without RecordedTail.Clear
        var elements = EvaluateFloatUnderScope(expression, 2, expectedIndex: 0);

        Assert.All(elements, static e => Assert.True(IsFloatZero(e)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void FloatSelectDivide_ZeroScalarUnderScope_ZerosOutput()
    {
        using var arr = CreateFloatArray([1, 2]);
        using var zero = GpuBigFloatTestHelpers.CreateFloat(_context, 0);
        using var expression = arr.AsVectorizedEnumerable().SelectDivide(zero);

        // mutant: FloatVectorBinaryScalarExpressionBase.Apply without RecordedTail.Clear
        var elements = EvaluateFloatUnderScope(expression, 2, expectedIndex: 0);

        Assert.All(elements, static e => Assert.True(IsFloatZero(e)));
    }

    // Evaluates the expression under a recording scope into a target of all-one words and returns its elements
    private BigInteger[] EvaluateIntUnderScope(IGpuBigIntVectorExpression expression, int count, int expectedIndex)
    {
        var words = expression.WordsPerElement;
        using var target = new GpuBigUIntArray(_context, count, words);
        FillOnes(target.DevicePointer, count * words);
        using (var scope = PendingThrows.Begin())
        {
            expression.Evaluate(_context, targetResult: target);
            Assert.Equal(expectedIndex, scope.Instance.MinIndex);
        }

        var host = ReadWords(target.DevicePointer, count * words);
        return Enumerable.Range(0, count)
            .Select(e => GpuBigFloatTestHelpers.FromWords(host[(e * words)..((e + 1) * words)]))
            .ToArray();
    }

    // The same for a float expression: each element as (significand words, exponent, sign)
    private (uint[] Significand, uint Exponent, uint Sign)[] EvaluateFloatUnderScope(
        IGpuBigFloatVectorExpression expression, int count, int expectedIndex)
    {
        var words = expression.WordsPerElement;
        using var significands = new GpuBigUIntArray(_context, count * words, 1);
        using var exponents = new GpuBigUIntArray(_context, count, 1);
        using var signs = new GpuBigUIntArray(_context, count, 1);
        FillOnes(significands.DevicePointer, count * words);
        FillOnes(exponents.DevicePointer, count);
        FillOnes(signs.DevicePointer, count);
        var target = new GpuBigFloatArrayView(
            new GpuBigUIntArrayView(_context, significands.Handle, count, words),
            new GpuBigUIntArrayView(_context, exponents.Handle, count, 1),
            new GpuBigUIntArrayView(_context, signs.Handle, count, 1));
        using (var scope = PendingThrows.Begin())
        {
            expression.Evaluate(_context, 0, count, target);
            Assert.Equal(expectedIndex, scope.Instance.MinIndex);
        }

        var hostSignificands = ReadWords(significands.DevicePointer, count * words);
        var hostExponents = ReadWords(exponents.DevicePointer, count);
        var hostSigns = ReadWords(signs.DevicePointer, count);
        return Enumerable.Range(0, count)
            .Select(e => (hostSignificands[(e * words)..((e + 1) * words)], hostExponents[e], hostSigns[e]))
            .ToArray();
    }

    private static bool IsFloatZero((uint[] Significand, uint Exponent, uint Sign) element) =>
        element.Significand.All(static w => w == 0) && element.Exponent == 0 && element.Sign == 0;

    private void FillOnes(IntPtr destination, int words) =>
        _context.Provider.CopyHostToDevice(destination, Enumerable.Repeat(uint.MaxValue, words).ToArray(), words);

    private uint[] ReadWords(IntPtr source, int words)
    {
        var host = new uint[words];
        _context.Provider.CopyDeviceToHost(host, source, words);
        return host;
    }
}
