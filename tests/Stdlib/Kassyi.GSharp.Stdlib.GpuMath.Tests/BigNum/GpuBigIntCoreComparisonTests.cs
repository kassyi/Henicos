using static Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.GpuBigIntCoreTestHelpers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuBigIntCoreComparisonTests
{
    public GpuBigIntCoreComparisonTests()
    {
        CudaCapability.EnsureGpuAvailable();
        // CUDA context is initialized automatically upon first kernel launch if supported.
        // Do not initialize here to prevent xUnit discovery crashes on non-GPU CI runners.
    }

    [SkippableFact]
    [Category(TestCategory.Benchmark)]
    public void CompareTo_ReductionFlagsAndExtrema_MatchesExpectedOrdering()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        // Typical case
        using var a = new GpuBigUInt(ctx, new uint[] { 100, 200, 300, 400 });
        using var b = new GpuBigUInt(ctx, new uint[] { 100, 200, 300, 400 });
        using var c = new GpuBigUInt(ctx, new uint[] { 101, 200, 300, 400 });

        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal(-1, a.CompareTo(c));
        Assert.Equal(1, c.CompareTo(a));

        // Boundary case 1: Zero limbs comparison
        using var zero1 = new GpuBigUInt(ctx, new uint[] { 0, 0, 0, 0 });
        using var zero2 = new GpuBigUInt(ctx, new uint[] { 0, 0, 0, 0 });
        Assert.Equal(0, zero1.CompareTo(zero2));

        // Boundary case 2: Max value limbs (0xFFFFFFFF)
        using var max1 = new GpuBigUInt(ctx, new uint[] { uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue });
        using var max2 = new GpuBigUInt(ctx, new uint[] { uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue });
        using var maxSub1 = new GpuBigUInt(ctx,
            new uint[] { uint.MaxValue - 1, uint.MaxValue, uint.MaxValue, uint.MaxValue });
        Assert.Equal(0, max1.CompareTo(max2));
        Assert.Equal(1, max1.CompareTo(maxSub1));
        Assert.Equal(-1, maxSub1.CompareTo(max1));

        // Boundary case 3: High-limb vs low-limb dominance
        using var highDominant = new GpuBigUInt(ctx, new uint[] { 0, 0, 0, 1 });
        using var lowDominant = new GpuBigUInt(ctx, new uint[] { uint.MaxValue, uint.MaxValue, uint.MaxValue, 0 });
        Assert.Equal(1, highDominant.CompareTo(lowDominant));
        Assert.Equal(-1, lowDominant.CompareTo(highDominant));
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void ComparisonOperators_RandomOperands_MatchesExpectedBooleans(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 70);
        var arrB = GenerateRandomArray(TestSizeWords, 70); // Same for equal tests
        var arrC = GenerateRandomArray(TestSizeWords, 71);
        arrA[TestSizeWords - 1] |= 0x40000000;
        arrB[TestSizeWords - 1] |= 0x40000000; // arrB must equal arrA
        arrC[TestSizeWords - 1] &= 0x3FFFFFFF; // Force A > C

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuC = new GpuBigUInt(context, arrC);

        // Equal / NotEqual
        Assert.True(gpuA == gpuB);
        Assert.False(gpuA != gpuB);
        Assert.False(gpuA == gpuC);
        Assert.True(gpuA != gpuC);

        // Greater / Less
        Assert.True(gpuA > gpuC);
        Assert.False(gpuC > gpuA);
        Assert.True(gpuC < gpuA);
        Assert.False(gpuA < gpuC);

        // GreaterEqual / LessEqual
        Assert.True(gpuA >= gpuB);
        Assert.True(gpuA <= gpuB);
        Assert.True(gpuA >= gpuC);
        Assert.True(gpuC <= gpuA);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void ComparisonOperators_ZeroAndLsbMsbDifferences_MatchesExpectedBooleans(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);

        // Edge Case: All zeros
        var arrZero1 = new uint[TestSizeWords];
        var arrZero2 = new uint[TestSizeWords];

        // Edge Case: Only LSB differs (requires scanning down to the very first bit)
        var arrLsb1 = GenerateRandomArray(TestSizeWords, 99);
        var arrLsb2 = arrLsb1.ToArray();
        arrLsb1[0] |= 1u;
        arrLsb2[0] &= ~1u; // Force arrLsb1 > arrLsb2 by 1 bit at the lowest position

        // Edge Case: Only MSB differs
        var arrMsb1 = GenerateRandomArray(TestSizeWords, 100);
        var arrMsb2 = arrMsb1.ToArray();
        arrMsb1[TestSizeWords - 1] |= 0x80000000;
        arrMsb2[TestSizeWords - 1] &= 0x7FFFFFFF;

        using var gpuZero1 = new GpuBigUInt(context, arrZero1);
        using var gpuZero2 = new GpuBigUInt(context, arrZero2);

        using var gpuLsb1 = new GpuBigUInt(context, arrLsb1);
        using var gpuLsb2 = new GpuBigUInt(context, arrLsb2);

        using var gpuMsb1 = new GpuBigUInt(context, arrMsb1);
        using var gpuMsb2 = new GpuBigUInt(context, arrMsb2);

        // Zeros
        Assert.True(gpuZero1 == gpuZero2);
        Assert.False(gpuZero1 > gpuZero2);
        Assert.False(gpuZero1 < gpuZero2);

        // LSB
        Assert.True(gpuLsb1 > gpuLsb2);
        Assert.True(gpuLsb2 < gpuLsb1);
        Assert.False(gpuLsb1 == gpuLsb2);

        // MSB
        Assert.True(gpuMsb1 > gpuMsb2);
        Assert.True(gpuMsb2 < gpuMsb1);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void CompareTo_RandomOperands_MatchesExpectedOrdering(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 20);
        var arrB = GenerateRandomArray(TestSizeWords, 20); // Same array

        var arrC = GenerateRandomArray(TestSizeWords, 21);
        // Force A > C
        arrA[TestSizeWords - 1] |= 0x40000000;
        arrC[TestSizeWords - 1] &= 0x3FFFFFFF;

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuC = new GpuBigUInt(context, arrC);

        Assert.Equal(0, gpuA.CompareTo(gpuB));
        Assert.Equal(1, gpuA.CompareTo(gpuC));
        Assert.Equal(-1, gpuC.CompareTo(gpuA));
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void LengthProperties_RandomAndZeroOperands_MatchesBitAndByteCounts(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 80);

        // To make testing exact sizes easier, set specific top bit
        arrA[TestSizeWords - 1] = 0;
        arrA[TestSizeWords - 2] = 0x80000000; // Exact length
        var bigA = ToBigInteger(arrA);

        using var gpuA = new GpuBigUInt(context, arrA);

        long expectedBits = bigA.GetBitLength();
        long expectedBytes = bigA.GetByteCount(isUnsigned: true);

        Assert.Equal(expectedBits, gpuA.GetBitLength());
        Assert.Equal(expectedBytes, gpuA.GetByteCount());

        // Zero-value coverage
        var zeroArr = new uint[TestSizeWords];
        using var gpuZero = new GpuBigUInt(context, zeroArr);
        var bigZero = BigInteger.Zero;

        Assert.Equal(bigZero.GetBitLength(), gpuZero.GetBitLength());
        Assert.Equal(bigZero.GetByteCount(isUnsigned: true), gpuZero.GetByteCount());
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void GetBitLength_StressRandomInputs_MatchesBigIntegerLengths(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        const int stressWords = 65536;
        using var context = new GpuWorkspace(stressWords, engine);
        var arr = new uint[stressWords];
        var rnd = new Random(42);
        for (int i = 0; i < stressWords / 2; i++)
        {
            arr[i] = (uint)rnd.Next();
        }

        int targetWord = stressWords / 2 - 100;
        arr[targetWord] = 0x40000000;
        for (int i = targetWord + 1; i < stressWords; i++)
        {
            arr[i] = 0;
        }

        using var gpu = new GpuBigUInt(context, arr);
        var bigInt = ToBigInteger(arr);

        Assert.Equal(bigInt.GetBitLength(), gpu.GetBitLength());
        Assert.Equal(bigInt.GetByteCount(isUnsigned: true), gpu.GetByteCount());
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void StateProperties_ZeroOneAndSignedValues_MatchesExpectedFlags(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);
        var zeroArr = new uint[TestSizeWords];
        var oneArr = new uint[TestSizeWords];
        oneArr[0] = 1;
        var evenArr = new uint[TestSizeWords];
        evenArr[0] = 2; // Even
        var negativeArr = new uint[TestSizeWords];
        negativeArr[TestSizeWords - 1] = 0x80000000; // Negative flag

        using var gpuZero = new GpuBigUInt(context, zeroArr);
        using var gpuOne = new GpuBigUInt(context, oneArr);
        using var gpuEven = new GpuBigUInt(context, evenArr);
        using var gpuNegative = new GpuBigUInt(context, negativeArr);

        Assert.True(gpuZero.IsZero());
        Assert.False(gpuOne.IsZero());

        Assert.True(gpuOne.IsOne());
        Assert.False(gpuZero.IsOne());

        Assert.True(gpuZero.GetIsEven());
        Assert.False(gpuOne.GetIsEven());
        Assert.True(gpuEven.GetIsEven());

        Assert.True(gpuNegative.GetIsNegative());
        Assert.False(gpuZero.GetIsNegative());

        // Test setter
        gpuZero.SetIsNegative(true);
        Assert.True(gpuZero.GetIsNegative());
        gpuZero.SetIsNegative(false);
        Assert.False(gpuZero.GetIsNegative());
    }

    // Comparing operands of different word counts reads only the words of each operand: the shorter one is
    // padded with zeros, not extended into whatever memory follows it.
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void CompareTo_ShorterOperand_IgnoresMemoryPastItsWords()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);

        // An 8-word buffer [v0, v1, garbage x 6]; `shortView` is a 2-word view of its start.
        using var backing = new GpuBigUInt(
            ctx, [5u, 7u, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu]);
        // The view does not own the buffer (disposing it frees nothing), so it is not disposed.
        var shortView = new GpuBigUInt(ctx, 2, backing.DevicePointer);
        using var equal = new GpuBigUInt(ctx, [5u, 7u, 0u, 0u]);
        using var larger = new GpuBigUInt(ctx, [6u, 7u, 0u, 0u]);

        // mutant: KernelBasicMathHandler.cs:172 / 177 `<` -> `>` / negated, L174 / 179 PadTo removed,
        // L184 / 185 the padded pointer replaced by the operand's own
        Assert.Equal(0, shortView.CompareTo(equal));
        Assert.Equal(0, equal.CompareTo(shortView));
        Assert.Equal(-1, shortView.CompareTo(larger));
        Assert.Equal(1, larger.CompareTo(shortView));
    }
}
