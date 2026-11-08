
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuRadixConverterTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuRadixConverterTests()
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
    public void Test_ConvertAndWrite()
    {
        CudaCapability.EnsureGpuAvailable();

        using var value = new GpuBigUInt(_context, new uint[] { 12345 });
        using var sw = new StringWriter();

        GpuRadixConverter.ConvertAndWrite(_context, value, 5, sw, true);
        
        var output = sw.ToString();
        Assert.Equal("1.2345", output);
    }

    [SkippableFact]
    public void Test_ConvertAndWrite_Large()
    {
        CudaCapability.EnsureGpuAvailable();

        using var value = new GpuBigUInt(_context, new uint[] { 1 });
        using var sw = new StringWriter();

        // 10001 digits, which forces the GPU Divide & Conquer loop
        GpuRadixConverter.ConvertAndWrite(_context, value, 10001, sw, false);
        
        var output = sw.ToString();
        Assert.Equal(10001, output.Length);
        Assert.EndsWith("1", output);
        Assert.StartsWith("00000", output);
    }

    // ---- Arguments, ownership and the D&C word count (#598 stage 2b) ----

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void ConvertAndWrite_InvalidArguments_Throw()
    {
        using var value = new GpuBigUInt(_context, new uint[] { 12345 });
        using var sw = new StringWriter();

        // A 5-digit value takes the leaf path, which never uses the workspace, so only the argument check rejects null.
        // mutant: GpuRadixConverter.cs:56 `ArgumentNullException.ThrowIfNull(workspace)` removed
        var nullError = Assert.Throws<ArgumentNullException>(() => GpuRadixConverter.ConvertAndWrite(null, value, 5, sw));
        Assert.Equal("workspace", nullError.ParamName);

        // mutant: GpuRadixConverter.cs:57 `ArgumentNullException.ThrowIfNull(writer)` removed (the leaf throws a NullReferenceException)
        var writerError = Assert.Throws<ArgumentNullException>(() => GpuRadixConverter.ConvertAndWrite(_context, value, 5, null));
        Assert.Equal("writer", writerError.ParamName);

        // At least one digit is written.
        // mutant: GpuRadixConverter.cs:58 `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetDigits)` removed
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuRadixConverter.ConvertAndWrite(_context, value, 0, sw));
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuRadixConverter.ConvertAndWrite(_context, value, -1, sw));
        Assert.Equal(string.Empty, sw.ToString());
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(12345u, 5)]
    [InlineData(1u, 10001)]
    public void ConvertAndWrite_DoesNotDisposeCallerValue(uint word, int targetDigits)
    {
        // The caller keeps the value: after the conversion it still holds the same number (a freed buffer is zeroed
        // and returned to the pool). 5 digits is a leaf; 10001 digits splits on the GPU first.
        // mutant: GpuRadixConverter.cs:69 `OwnsValue = false` -> `OwnsValue = true`
        using var value = new GpuBigUInt(_context, new uint[] { word });
        using var sw = new StringWriter();

        GpuRadixConverter.ConvertAndWrite(_context, value, targetDigits, sw);

        Assert.Equal(new BigInteger(word), value.ToBigInteger());
    }

    // With isMostSignificant the text is the fixed-point value value / 10^(targetDigits - 1): exactly targetDigits - 1
    // digits after the point and at least one before it, zero padded when the value is shorter, whether targetDigits
    // takes the leaf path (up to 10000 digits) or splits on the GPU first. Without it the value is zero padded to
    // targetDigits digits. The expected text is built from the BigInteger of the test.
    // mutant: GpuRadixConverter.cs FormatLeafNode `if (str.Length < targetDigits)` -> `if (!isMostSignificant &&
    // str.Length < targetDigits)` (the shorter values with isMostSignificant, rows 6 to 20000 and "0" with 12 and
    // 10001: "1.2345" and a 10002-character "0.000...12345")
    // mutant: FormatLeafNode `integerDigits = str.Length - (targetDigits - 1)` -> `integerDigits = 1` (the longer
    // values, rows 1 and 3 and the 25000-digit value of the next test)
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData("12345", 1, true)]
    [InlineData("12345", 3, true)]
    [InlineData("12345", 5, true)]
    [InlineData("12345", 6, true)]
    [InlineData("12345", 10, true)]
    [InlineData("12345", 10000, true)]
    [InlineData("12345", 10001, true)]
    [InlineData("12345", 20000, true)]
    [InlineData("0", 1, true)]
    [InlineData("0", 12, true)]
    [InlineData("0", 10001, true)]
    [InlineData("12345", 10, false)]
    [InlineData("12345", 20000, false)]
    [InlineData("12345", 3, false)]
    public void ConvertAndWrite_ShortOrLongValue_WritesTheSameFixedPointTextOnBothPaths(
        string decimalValue,
        int targetDigits,
        bool isMostSignificant)
    {
        var expected = BigInteger.Parse(decimalValue);
        using var value = GpuBigUInt.FromBigInteger(_context, expected);
        using var sw = new StringWriter();

        GpuRadixConverter.ConvertAndWrite(_context, value, targetDigits, sw, isMostSignificant);

        Assert.Equal(FixedPointText(expected, targetDigits, isMostSignificant), sw.ToString());
    }

    // A value with more digits than targetDigits above the leaf threshold: the integer part takes the extra digits.
    // mutant: FormatLeafNode `integerDigits = 1` (the integer part becomes one digit)
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ConvertAndWrite_ValueLongerThanTargetDigits_PutsTheExtraDigitsBeforeThePoint()
    {
        const int Digits = 25000;
        const int TargetDigits = 20000;
        var random = new Random(1332);
        var digits = new char[Digits];
        digits[0] = (char)('1' + random.Next(9));
        for (var i = 1; i < Digits; i++)
        {
            digits[i] = (char)('0' + random.Next(10));
        }

        var expected = BigInteger.Parse(new string(digits));
        using var value = GpuBigUInt.FromBigInteger(_context, expected);
        using var sw = new StringWriter();

        GpuRadixConverter.ConvertAndWrite(_context, value, TargetDigits, sw, isMostSignificant: true);

        var text = sw.ToString();
        Assert.Equal(FixedPointText(expected, TargetDigits, true), text);
        // 5001 digits before the point, 19999 after it.
        Assert.Equal(Digits - TargetDigits + 1, text.IndexOf('.'));
    }

    // value / 10^(targetDigits - 1) with exactly targetDigits - 1 fraction digits, or value padded to targetDigits.
    private static string FixedPointText(BigInteger value, int targetDigits, bool isMostSignificant)
    {
        var text = value.ToString().PadLeft(targetDigits, '0');
        if (!isMostSignificant)
        {
            return text;
        }

        var integerDigits = text.Length - (targetDigits - 1);
        return text[..integerDigits] + "." + text[integerDigits..];
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertAndWrite_LargeRandomValue_MatchesBigIntegerToString(bool isMostSignificant)
    {
        // A 25000-digit value (about 2600 words) goes through the divide-and-conquer path. Its bit length is not a
        // multiple of 32, so the word count is rounded up from a partial top word.
        // mutant: GpuRadixConverter.cs:78 `+ 31` -> `- 31` (the top word is cut off by PadTo)
        // mutant: GpuRadixConverter.cs:79 `actualWords == 0` -> `!=` (one word)
        const int Digits = 25000;
        var random = new Random(598);
        var digits = new char[Digits];
        digits[0] = (char)('1' + random.Next(9));
        for (var i = 1; i < Digits; i++)
        {
            digits[i] = (char)('0' + random.Next(10));
        }

        var expected = BigInteger.Parse(new string(digits));
        // Witness: the top word is partial.
        Assert.InRange((int)(expected.GetBitLength() % 32), 1, 31);

        using var value = GpuBigUInt.FromBigInteger(_context, expected);
        using var sw = new StringWriter();

        GpuRadixConverter.ConvertAndWrite(_context, value, Digits, sw, isMostSignificant);

        var text = expected.ToString();
        Assert.Equal(isMostSignificant ? text[..1] + "." + text[1..] : text, sw.ToString());
    }
}
