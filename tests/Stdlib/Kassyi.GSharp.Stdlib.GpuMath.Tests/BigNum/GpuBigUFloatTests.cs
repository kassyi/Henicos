
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuBigUFloatTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigUFloatTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Normalize_And_Divide()
    {
        CudaCapability.EnsureGpuAvailable();

        using var sig = new GpuBigUInt(_context, new uint[] { 10 });
        using var a = new GpuBigUFloat(sig, 0);

        using var normalized = a.Normalize(0, false);
        Assert.Equal(0, normalized.Exponent);

        var (q, r) = a.DivideByUInt32(2);
        q.Dispose();
    }

    [SkippableFact]
    public void Test_Expressions()
    {
        CudaCapability.EnsureGpuAvailable();

        using var aSig = new GpuBigUInt(_context, new uint[] { 4 });
        using var bSig = new GpuBigUInt(_context, new uint[] { 2 });
        using var a = new GpuBigUFloat(aSig, 0);
        using var b = new GpuBigUFloat(bSig, 0);

        var expr1 = a + b;
        var expr2 = a - b;
        var expr3 = a * b;
        var expr4 = a / b;
        
        // These rely on implicit operators and Evaluate!
        GpuBigUFloat result1 = expr1;
        GpuBigUFloat result2 = expr2;
        GpuBigUFloat result3 = expr3;
        GpuBigUFloat result4 = expr4;
        
        result1.Dispose();
        result2.Dispose();
        result3.Dispose();
        result4.Dispose();
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DirectArithmetic_And_Comparison()
    {
        CudaCapability.EnsureGpuAvailable();

        using var aSig = new GpuBigUInt(_context, new uint[] { 12 });
        using var bSig = new GpuBigUInt(_context, new uint[] { 3 });
        using var cSig = new GpuBigUInt(_context, new uint[] { 3 });
        using var a = new GpuBigUFloat(aSig, 0);
        using var b = new GpuBigUFloat(bSig, 0);
        using var c = new GpuBigUFloat(cSig, 2); // 3 * 2^2 = 12

        using var sum = a.Add(b);
        using var diff = a.Subtract(b);
        using var prod = a.Multiply(b);
        using var quot = a.Divide(b);

        double ToDouble(GpuBigUFloat v) => (double)v.Significand.ToBigInteger() * Math.Pow(2.0, v.Exponent);

        Assert.Equal(15.0, ToDouble(sum), precision: 8);
        Assert.Equal(9.0, ToDouble(diff), precision: 8);
        Assert.Equal(36.0, ToDouble(prod), precision: 8);
        Assert.Equal(4.0, ToDouble(quot), precision: 8);

        // Zero-allocation CompareTo across different exponents (12 * 2^0 == 3 * 2^2). Equals is the identity of the
        // significand buffer and the exponent (#638), so two equal values in separate buffers are == but not Equals.
        Assert.Equal(0, a.CompareTo(c));
        Assert.False(a.Equals(c));
        Assert.False(a.Equals((object)c));
        Assert.True(a == c);
        Assert.False(a != c);
        Assert.True(a <= c);
        Assert.True(a >= c);

        Assert.True(a > b);
        Assert.True(b < a);
        Assert.False(a.Equals(b));
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Test_NotImplementedMethods()
    {
        CudaCapability.EnsureGpuAvailable();

        using var aSig = new GpuBigUInt(_context, new uint[] { 4 });
        using var a = new GpuBigUFloat(aSig, 0);

        Assert.Throws<NotImplementedException>(() => a.AlignRadixPoint(a));
        Assert.Throws<NotImplementedException>(() => a.Round(GpuBigFloatRoundingMode.RoundToNearestEven, false, false, false));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigUFloat_EffectiveScale_Create_StrictCompare()
    {
        CudaCapability.EnsureGpuAvailable();

        // The value 12 * 2^-3 = 1.5 lies in [2^0, 2^1): EffectiveScale = bit length 4 + exponent -3 = 1.
        // mutant: GpuBigUFloat.cs:46 `Exponent + GetBitLength()` -> `Exponent - GetBitLength()`
        using var twelveSig = new GpuBigUInt(_context, [12u]);
        using var scaled = new GpuBigUFloat(twelveSig, -3);
        Assert.Equal(1L, scaled.EffectiveScale);

        // An unsigned value cannot be negative, except zero, whose sign carries no value.
        // mutant: GpuBigUFloat.cs:57 `isNegative && !IsZero()` -> `isNegative || !IsZero()` / `isNegative && IsZero()`,
        // L59 `throw` removed
        using var nonZero = new GpuBigUInt(_context, [7u]);
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuBigUFloat.Create(nonZero, 0, isNegative: true));
        using var zero = new GpuBigUInt(_context, [0u]);
        var negativeZero = GpuBigUFloat.Create(zero, 0, isNegative: true);
        Assert.False(negativeZero.IsNegative);
        var positive = GpuBigUFloat.Create(nonZero, 0, isNegative: false);
        Assert.Equal(new BigInteger(7), positive.Significand.ToBigInteger());

        // Equal values with different representations are neither greater nor less than each other.
        // mutant: GpuBigUFloat.cs:275 `CompareTo(right) > 0` -> `>= 0` (and the same for `<` at L267)
        using var aSig = new GpuBigUInt(_context, [12u]);
        using var cSig = new GpuBigUInt(_context, [3u]);
        using var a = new GpuBigUFloat(aSig, 0);
        using var c = new GpuBigUFloat(cSig, 2);
        Assert.False(a > c);
        Assert.False(a < c);
        Assert.False(c > a);
        Assert.False(c < a);
    }

    public static TheoryData<uint, int, int, bool, uint, int> NormalizeCases => new()
    {
        // No carry and no shift keeps the value and its exponent.
        // mutant: GpuBigUFloat.cs:133 `IsZero() && !carryOut` -> `IsZero() || !carryOut` / `!(...)` (returns exponent 0)
        { 10u, 5, 0, false, 10u, 5 },
        // Zero without a carry becomes the canonical zero with exponent 0.
        // mutant: GpuBigUFloat.cs:133 `!carryOut` -> `carryOut`, L134 block removed
        { 0u, 7, 0, false, 0u, 0 },
        // A carry out halves the significand and raises the exponent by one.
        // mutant: GpuBigUFloat.cs:143 `>> 1` -> `<< 1`, L147 `Exponent + 1` -> `Exponent - 1`
        { 10u, 5, 0, true, 5u, 6 },
        // Leading zeros shift the significand left and lower the exponent by the same amount.
        // mutant: GpuBigUFloat.cs:152 `<< leadingZeros` -> `>> leadingZeros`, L156 `Exponent - leadingZeros` -> `+`
        { 10u, 5, 3, false, 80u, 2 },
    };

    // Normalize keeps the value sig * 2^exp and changes only its representation (canonical zero has exponent 0).
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(NormalizeCases))]
    public void GpuBigUFloat_Normalize_Table(
        uint sig, int exp, int leadingZeros, bool carryOut, uint expectedSig, int expectedExp)
    {
        CudaCapability.EnsureGpuAvailable();

        using var sigBuffer = new GpuBigUInt(_context, [sig, 0u]);
        var value = new GpuBigUFloat(sigBuffer, exp);

        // The result may share the significand buffer with the input; sigBuffer owns it.
        var normalized = value.Normalize(leadingZeros, carryOut);
        try
        {
            Assert.Equal(new BigInteger(expectedSig), normalized.Significand.ToBigInteger());
            Assert.Equal(expectedExp, normalized.Exponent);
        }
        finally
        {
            if (normalized.Significand.DevicePointer != sigBuffer.DevicePointer)
            {
                normalized.Dispose();
            }
        }
    }

    // Dividing by a scalar divides the significand and keeps the exponent: 10 * 2^3 / 3 = 3 * 2^3 remainder 1 * 2^3.
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigUFloat_DivideByUInt32_KeepsExponent()
    {
        CudaCapability.EnsureGpuAvailable();

        using var sig = new GpuBigUInt(_context, [10u]);
        var value = new GpuBigUFloat(sig, 3);

        // mutant: GpuBigUFloat.cs:169 body removed / `Exponent` replaced
        var (quotient, remainder) = value.DivideByUInt32(3);
        using (quotient)
        {
            Assert.Equal(new BigInteger(3), quotient.Significand.ToBigInteger());
            Assert.Equal(3, quotient.Exponent);
            Assert.Equal(1u, remainder);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Equals_IsIdentity_EqualityOperatorComparesValues()
    {
        // The float types follow the integer rule (#638): Equals and GetHashCode compare the significand buffer, the
        // exponent and the sign without reading the device; == compares values (12 * 2^0 == 3 * 2^2).
        // mutant: GpuBigUFloat.Equals compares the values again (a.Equals(c) becomes true)
        // mutant: GpuBigUFloat.Equals ignores Exponent (a.Equals(shifted) becomes true)
        // mutant: GpuBigFloat.Equals ignores IsNegative (fa.Equals(negated) becomes true)
        // mutant: GpuBigUFloat / GpuBigFloat operator == calls Equals (a == c becomes false)
        CudaCapability.EnsureGpuAvailable();
        using var aSig = new GpuBigUInt(_context, [12u]);
        using var cSig = new GpuBigUInt(_context, [3u]);
        var a = new GpuBigUFloat(aSig, 0);
        var c = new GpuBigUFloat(cSig, 2);
        var copy = a;
        var shifted = new GpuBigUFloat(aSig, 1);
        var fa = new GpuBigFloat(a, false);
        var fc = new GpuBigFloat(c, false);
        var negated = new GpuBigFloat(a, true);

        Assert.Equal(
            (false, true, true, true, false, 2),
            (a.Equals(c), a == c, a.Equals(copy), a.GetHashCode() == copy.GetHashCode(), a.Equals(shifted),
                new HashSet<GpuBigUFloat> { a, c, copy }.Count)
        );
        Assert.Equal(
            (false, true, true, false, 2),
            (fa.Equals(fc), fa == fc, fa.Equals(new GpuBigFloat(a, false)), fa.Equals(negated),
                new HashSet<GpuBigFloat> { fa, fc, new GpuBigFloat(a, false) }.Count)
        );
    }
}
