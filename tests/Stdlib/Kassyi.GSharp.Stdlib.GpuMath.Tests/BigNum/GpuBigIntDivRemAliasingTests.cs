#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Division, modulo and square root whose output is one of their inputs (#636, design gpumath-m1-misc 4.2.3): as GMP's
/// <c>mpz_tdiv_qr</c>, an input that shares memory with an output is copied first, so the result is the one of separate
/// buffers. A quotient and a remainder in the same memory have no meaning and are rejected.
/// </summary>
/// <remarks>
/// Each case names the branch of <c>DivRem</c> it reaches (coverage witness). The expected values come from
/// <see cref="BigInteger"/>; the random inputs use a fixed seed that the failure message shows.
/// </remarks>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuBigIntDivRemAliasingTests : IDisposable
{
    private const int Words = 64;
    private const int Seed = 636;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigIntDivRemAliasingTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    public enum Divisor
    {
        // 32-word dividend, 16-word odd divisor: the Barrett reduction (Reduce reads the dividend and the divisor again
        // after it wrote the quotient)
        Barrett,

        // 7: a divisor of one word that is not a power of two (DivideByUInt32 gets the same pointer twice)
        SmallWord,

        // 8: a one-word power of two (the remainder is read from the dividend after the shift)
        SmallPowerOfTwo,

        // 2^40: a multi-word power of two (the remainder words are copied from the dividend after the shift)
        WidePowerOfTwo,

        // a = 5, b = 2^100: a < b (the quotient is cleared and the dividend is the remainder)
        DividendSmaller
    }

    public enum Alias
    {
        QuotientIsDividend,
        QuotientIsDivisor,
        RemainderIsDividend,
        RemainderIsDivisor
    }

    public static TheoryData<Divisor, Alias> DivRemCases()
    {
        var data = new TheoryData<Divisor, Alias>();
        foreach (var divisor in Enum.GetValues<Divisor>())
        {
            foreach (var alias in Enum.GetValues<Alias>())
            {
                data.Add(divisor, alias);
            }
        }

        return data;
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(DivRemCases))]
    public void DivRem_OutputIsAnInput_GivesTheSeparateBufferResult(Divisor divisor, Alias alias)
    {
        // P1 / P3: the quotient and the remainder equal BigInteger.DivRem of the original values, also when one output
        // is the dividend or the divisor; P2: the input that is not an output keeps its value.
        // mutant: DivRem without the copy of an overlapping dividend (Barrett: System.Exception after the correction
        // loop; the power-of-two and a < b branches: a wrong remainder)
        // mutant: DivRem without the copy of an overlapping divisor (QuotientIsDivisor / RemainderIsDivisor)
        var (a0, b0) = Values(divisor);
        using var a = GpuBigUInt.FromBigInteger(_workspace, a0, Words);
        using var b = GpuBigUInt.FromBigInteger(_workspace, b0, Words);
        using var other = new GpuBigUInt(_workspace, Words);

        switch (alias)
        {
            case Alias.QuotientIsDividend:
                GpuBigIntDivRem.DivRem(a, b, a, other);
                break;
            case Alias.QuotientIsDivisor:
                GpuBigIntDivRem.DivRem(a, b, b, other);
                break;
            case Alias.RemainderIsDividend:
                GpuBigIntDivRem.DivRem(a, b, other, a);
                break;
            default:
                GpuBigIntDivRem.DivRem(a, b, other, b);
                break;
        }

        var q0 = BigInteger.DivRem(a0, b0, out var r0);
        var (quotient, remainder, untouched, untouchedExpected) = alias switch
        {
            Alias.QuotientIsDividend => (a, other, b, b0),
            Alias.QuotientIsDivisor => (b, other, a, a0),
            Alias.RemainderIsDividend => (other, a, b, b0),
            _ => (other, b, a, a0)
        };

        var message = $"{divisor} {alias} seed {Seed}";
        Assert.True(q0 == quotient.ToBigInteger(), $"quotient: {message}");
        Assert.True(r0 == remainder.ToBigInteger(), $"remainder: {message}");
        Assert.True(untouchedExpected == untouched.ToBigInteger(), $"input changed: {message}");
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void DivAndMod_ResultIsAnInput_GiveTheSeparateBufferResult(bool intoDivisor)
    {
        // Div and Mod go through DivRem, so Div(a, b, a), Div(a, b, b), Mod(a, b, a) and Mod(a, b, b) are guarded too
        // (the #636 repro is Div(a, b, a))
        var (a0, b0) = Values(Divisor.Barrett);
        using var a = GpuBigUInt.FromBigInteger(_workspace, a0, Words);
        using var b = GpuBigUInt.FromBigInteger(_workspace, b0, Words);
        using var a2 = GpuBigUInt.FromBigInteger(_workspace, a0, Words);
        using var b2 = GpuBigUInt.FromBigInteger(_workspace, b0, Words);

        GpuBigIntDivRem.Div(a, b, intoDivisor ? b : a);
        GpuBigIntDivRem.Mod(a2, b2, intoDivisor ? b2 : a2);

        Assert.Equal(a0 / b0, (intoDivisor ? b : a).ToBigInteger());
        Assert.Equal(a0 % b0, (intoDivisor ? b2 : a2).ToBigInteger());
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(64)]
    [InlineData(2)]
    [InlineData(1)]
    public void Sqrt_ResultIsTheOperand_GivesTheSeparateBufferResult(int valueWords)
    {
        // 64 words: Heron's refinement reads the operand on every step; 2 words (2^62 + 5): just above one word; 1 word
        // (15): the single-word path. Every path reads the operand completely before it writes the result, so Sqrt needs
        // no copy; this test keeps that true.
        // mutant: the single-word path clears the result before it reads the operand
        var value = valueWords switch
        {
            64 => RandomValue(new Random(Seed), 64 * 32 - 3),
            2 => (BigInteger.One << 62) + 5,
            _ => new BigInteger(15)
        };
        using var a = GpuBigUInt.FromBigInteger(_workspace, value, Words);

        GpuBigIntDivRem.Sqrt(a, a);

        Assert.Equal(BigFloatReference.IntegerSqrt(value), a.ToBigInteger());
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void DivRem_QuotientAndRemainderOverlap_IsRejected()
    {
        // Two results in one place have no meaning (design 4.2.1): ArgumentException naming the remainder buffer, for the
        // same buffer and for a buffer that overlaps part of it.
        // mutant: the quotient / remainder overlap check removed
        // mutant: Overlaps compares one side only (the partial overlap below starts after the quotient)
        using var a = GpuBigUInt.FromBigInteger(_workspace, 1000, Words);
        using var b = GpuBigUInt.FromBigInteger(_workspace, 7, Words);
        using var q = new GpuBigUInt(_workspace, 2 * Words);
        GpuBigUIntView whole = q;
        var head = whole.Slice(0, Words);
        var tail = whole.Slice(Words / 2, Words);

        var same = Assert.Throws<ArgumentException>(() => DivRemInto(a, b, q, q));
        var partial = PartialDivRemThrows(a, b, head, tail);

        Assert.Equal("remainderBuffer", same.ParamName);
        Assert.Equal("remainderBuffer", partial);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void Reduce_OutputOverlapsAnInput_IsRejected()
    {
        // Reduce and ReduceZeroSync are the building blocks under DivRem and ModPow; they do not copy, so an output that
        // overlaps an input is an ArgumentException instead of a wrong value (design 4.2.3).
        // mutant: the Reduce overlap check removed
        using var a = GpuBigUInt.FromBigInteger(_workspace, 1000, Words);
        using var b = GpuBigUInt.FromBigInteger(_workspace, 7, Words);
        using var reciprocal = new GpuBigUInt(_workspace, Words);
        using var r = new GpuBigUInt(_workspace, Words);

        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.Reduce(a, b, reciprocal, 64, 1, a, r));
        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.Reduce(a, b, reciprocal, 64, 1, r, reciprocal));
        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.ReduceZeroSync(a, b, reciprocal, 64, 1, r, b));
        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.ReduceZeroSync(a, b, reciprocal, 64, 1, r, r));
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void DivRem_SeparateBuffers_CopiesNothing()
    {
        // Adjacent, separate buffers are not an overlap (half-open ranges): DivRem allocates no copy of an input. The
        // slots are quotient, dividend, remainder, divisor, so each input starts where an output ends. The same division
        // first runs on slots with gaps between them, which sets the peak of live buffers DivRem needs by itself; a copy
        // would live through the whole call and raise it by one.
        // mutant: Overlaps with <= instead of < (each input touches the output before it)
        const int slot = 37;
        using (var spaced = Load(8 * slot, 2 * slot, 6 * slot))
        {
            GpuBigUIntView view = spaced;
            GpuBigIntDivRem.DivRem(view.Slice(2 * slot, slot), view.Slice(6 * slot, slot), view.Slice(0, slot),
                view.Slice(4 * slot, slot));
        }

        using var buffer = Load(4 * slot, slot, 3 * slot);
        GpuBigUIntView whole = buffer;
        var before = _workspace.GetMemoryStatistics();

        GpuBigIntDivRem.DivRem(whole.Slice(slot, slot), whole.Slice(3 * slot, slot), whole.Slice(0, slot),
            whole.Slice(2 * slot, slot));
        var after = _workspace.GetMemoryStatistics();

        Assert.Equal(new BigInteger(142), whole.Slice(0, slot).ToBigInteger());
        Assert.Equal(new BigInteger(6), whole.Slice(2 * slot, slot).ToBigInteger());
        Assert.Equal(before.PeakLiveBuffers, after.PeakLiveBuffers);
    }

    // One buffer of 'words' words with the dividend 1000 at word 'dividendAt' and the divisor 7 at word 'divisorAt'
    private GpuBigUInt Load(int words, int dividendAt, int divisorAt)
    {
        var buffer = new GpuBigUInt(_workspace, words);
        var host = new uint[words];
        host[dividendAt] = 1000;
        host[divisorAt] = 7;
        _workspace.Provider.CopyHostToDevice(buffer.DevicePointer, host, host.Length, _workspace.Stream);
        return buffer;
    }

    private static void DivRemInto(GpuBigUInt a, GpuBigUInt b, GpuBigUInt q, GpuBigUInt r) =>
        GpuBigIntDivRem.DivRem(a, b, q, r);

    // A view cannot be captured by a lambda, so Assert.Throws cannot take the slices
    private static string? PartialDivRemThrows(GpuBigUInt a, GpuBigUInt b, GpuBigUIntView q, GpuBigUIntView r)
    {
        try
        {
            GpuBigIntDivRem.DivRem(a, b, q, r);
            return "(no exception)";
        }
        catch (ArgumentException ex)
        {
            return ex.ParamName;
        }
    }

    private static (BigInteger A, BigInteger B) Values(Divisor divisor)
    {
        var random = new Random(Seed);
        return divisor switch
        {
            Divisor.Barrett => (RandomValue(random, 32 * 32), RandomValue(random, 16 * 32) | 1),
            Divisor.SmallWord => (RandomValue(random, 32 * 32), 7),
            // The low bits are not 0, so a remainder read from the overwritten dividend would differ
            Divisor.SmallPowerOfTwo => (RandomValue(random, 32 * 32) | 5, 8),
            Divisor.WidePowerOfTwo => (RandomValue(random, 32 * 32) | ((BigInteger.One << 39) | 3), BigInteger.One << 40),
            _ => (5, BigInteger.One << 100)
        };
    }

    // A value of exactly 'bits' bits
    private static BigInteger RandomValue(Random random, int bits)
    {
        var bytes = new byte[(bits + 7) / 8 + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        var value = new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
        return value | (BigInteger.One << (bits - 1));
    }
}
