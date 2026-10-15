using System.Collections.Concurrent;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms.References;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuBigIntCoreTests
{
    [SkippableFact]
    [Category(TestCategory.Alignment)]
    public void Test_MemoryPoolAllocation()
    {
        CudaCapability.EnsureGpuAvailable();
        // Stress test the internal pools (e.g. _carryBufferPool, _tempPool) to expose concurrency issues
        // across multiple concurrent threads sharing the same GpuWorkspace instance.
        var exceptions = new ConcurrentBag<Exception>();
        using var sharedCtx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        Parallel.For(0, 100, i =>
        {
            try
            {
                uint valA = (uint)i + 1;
                uint valB = (uint)i + 2;
                using var a = new GpuBigUInt(sharedCtx, new uint[] { valA, 0, 0, 0 });
                using var b = new GpuBigUInt(sharedCtx, new uint[] { valB, 0, 0, 0 });
                using var c = new GpuBigUInt(sharedCtx, 4);
                using var d = new GpuBigUInt(sharedCtx, 8);

                c.Value = a + b;
                d.Value = c * a;

                sharedCtx.Provider.SynchronizeStream(sharedCtx.Stream);

                var hostC = c.ToHostArray();
                var hostD = d.ToHostArray();
                Assert.Equal(valA + valB, hostC[0]);
                Assert.Equal(0u, hostC[1]);
                Assert.Equal((valA + valB) * valA, hostD[0]);
                Assert.Equal(0u, hostD[1]);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [SkippableFact]
    [Category(TestCategory.Benchmark)]
    public void Test_GpuReductionCompareAndFlags()
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

    private const int TestSizeWords = 256; // 8192 bits

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    public void Test_DirectSquarePointwiseKernel()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        using var a = new GpuBigUInt(ctx, new uint[] { 123456789, 0, 0, 0 });
        using var result = new GpuBigUInt(ctx, 8);

        // Should invoke direct square kernel instead of generic multiply
        result.Value = a * a;

        var expected = ToBigInteger(new uint[] { 123456789, 0, 0, 0 }) *
                       ToBigInteger(new uint[] { 123456789, 0, 0, 0 });
        Assert.Equal(expected, ToBigInteger(result.ToHostArray()));
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    public void Test_ParallelGcdLargeDigits()
    {
        CudaCapability.EnsureGpuAvailable();
        // Issue #112 & #117: Test large GCD to trigger pipeline stalls / TDR issues.
        using var ctx = new GpuWorkspace(128, GpuExecutionEngine.GSharpAot);

        var arrA = GenerateRandomArray(128, 42); // 4096-bit number
        var arrB = GenerateRandomArray(128, 43);

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);
        var expected = BigInteger.GreatestCommonDivisor(bigA, bigB);

        using var gpuA = new GpuBigUInt(ctx, arrA);
        using var gpuB = new GpuBigUInt(ctx, arrB);
        using var result = new GpuBigUInt(ctx, 128);

        GpuBigIntMath.Gcd(gpuA, gpuB, result);
        Assert.Equal(expected, ToBigInteger(result.ToHostArray()));
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    public void Test_ExpressionKernelFusion()
    {
        CudaCapability.EnsureGpuAvailable();
        // Expose missing kernel fusion & temporary memory leaks in ExpressionEvaluator.
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        using var a = new GpuBigUInt(ctx, new uint[] { 100, 0, 0, 0 });
        using var b = new GpuBigUInt(ctx, new uint[] { 200, 0, 0, 0 });
        using var c = new GpuBigUInt(ctx, new uint[] { 300, 0, 0, 0 });
        using var result = new GpuBigUInt(ctx, 8); // Mul requires a+b size

        // (a + b) * c
        result.Value = (a + b) * c;

        var expected = BigInteger.Parse("90000");
        Assert.Equal(expected, ToBigInteger(result.ToHostArray()));
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    [InlineData(GpuExecutionEngine.LegacyNative)]
    public void Test_Add_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        RunBinaryOpTest(
            engine,
            resultWords: TestSizeWords,
            contextWords: TestSizeWords * 2,
            prepareData: null,
            cpuFunc: (a, b) => a + b,
            gpuFunc: (a, b) => a + b
        );
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    [InlineData(GpuExecutionEngine.LegacyNative)]
    public void Test_Add_CarryPropagation_AllOnes(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2 + 8, engine); // Extra capacity just in case

        // Create an array with all bits set to 1, except the highest bit of the highest word
        // to ensure it is treated as a positive number by BigInteger.
        var arrA = new uint[TestSizeWords];
        Array.Fill(arrA, 0xFFFFFFFF);

        arrA[TestSizeWords - 1] = 0x7FFFFFFF;

        var arrB = new uint[TestSizeWords];
        arrB[0] = 1; // Ripple carry generator

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        gpuResult.Value = gpuA + gpuB;

        AssertGpuResultEqualsCpu(bigA + bigB, gpuResult, TestSizeWords);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    [InlineData(GpuExecutionEngine.LegacyNative)]
    public void Test_Sub_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        RunBinaryOpTest(
            engine,
            resultWords: TestSizeWords,
            contextWords: TestSizeWords * 2,
            prepareData: (arrA, arrB) =>
            {
                arrA[TestSizeWords - 1] |= 0x40000000;
                arrB[TestSizeWords - 1] &= 0x3FFFFFFF;
            },
            cpuFunc: (a, b) => a - b,
            gpuFunc: (a, b) => a - b
        );
    }


    public GpuBigIntCoreTests()
    {
        CudaCapability.EnsureGpuAvailable();
        // CUDA context is initialized automatically upon first kernel launch if supported.
        // Do not initialize here to prevent xUnit discovery crashes on non-GPU CI runners.
    }

    private uint[] GenerateRandomArray(int numWords, int seed = 42)
    {
        var rnd = new Random(seed);
        var result = new uint[numWords];
        for (int i = 0; i < numWords; i++)
        {
            result[i] = (uint)rnd.Next() | ((uint)rnd.Next() << 16);
        }

        // Clear highest bit of highest word to ensure positive BigInteger for simplicity
        result[numWords - 1] &= 0x7FFFFFFF;
        return result;
    }

    private BigInteger ToBigInteger(uint[] words)
    {
        var bytes = new byte[words.Length * 4 + 1]; // +1 for positive sign guarantee
        Buffer.BlockCopy(words, 0, bytes, 0, words.Length * 4);
        return new BigInteger(bytes);
    }

    private uint[] FromBigInteger(BigInteger b, int targetWords)
    {
        var bytes = b.ToByteArray();
        var words = new uint[targetWords];
        int copyBytes = Math.Min(bytes.Length, targetWords * 4);
        Buffer.BlockCopy(bytes, 0, words, 0, copyBytes);
        return words;
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Scalar_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 40);

        // To test carry propagation in Add, we can force the first word to be 0xFFFFFFFF
        arrA[0] = 0xFFFFFFFF;
        arrA[1] = 0xFFFFFFFF; // and the second to check multi-word propagation

        // To test borrow in Sub, we can force the first word to be 0
        var arrB = GenerateRandomArray(TestSizeWords, 41);
        arrB[0] = 0;
        arrB[1] = 0;
        // Prevent overall underflow
        arrB[TestSizeWords - 1] |= 0x40000000;

        uint scalarAdd = 10;
        uint scalarSub = 5;
        uint scalarMul = 12345;

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        // Scalar Add
        GpuBigIntMath.Add(gpuA, scalarAdd, gpuResult);
        AssertGpuResultEqualsCpu(bigA + scalarAdd, gpuResult, TestSizeWords);

        // Scalar Sub
        GpuBigIntMath.Sub(gpuB, scalarSub, gpuResult);
        AssertGpuResultEqualsCpu(bigB - scalarSub, gpuResult, TestSizeWords);

        // Scalar Mul
        GpuBigIntMath.Mul(gpuA, scalarMul, gpuResult);
        AssertGpuResultEqualsCpu(bigA * scalarMul, gpuResult, TestSizeWords);
    }

    [SkippableFact]
    public void Test_NttReference_Correctness()
    {
        CudaCapability.EnsureGpuAvailable();
        var rand = new Random(42);
        int testSize = 256; // 8192 bits
        var aWords = new uint[testSize];
        var bWords = new uint[testSize];
        for (int i = 0; i < testSize; i++)
        {
            aWords[i] = (uint)rand.Next() | ((uint)rand.Next() << 16);
            bWords[i] = (uint)rand.Next() | ((uint)rand.Next() << 16);
        }

        var aBig = ToBigInteger(aWords);
        var bBig = ToBigInteger(bWords);

        var expected = BigInteger.Multiply(aBig, bBig);
        var expectedBytes = expected.ToByteArray();
        var expectedWords = new uint[testSize * 2];
        Buffer.BlockCopy(expectedBytes, 0, expectedWords, 0,
            Math.Min(expectedBytes.Length, expectedWords.Length * 4));

        var actualWords = NttCpuReference.Multiply(aWords, bWords);

        for (int i = 0; i < expectedWords.Length; i++)
        {
            Assert.Equal(expectedWords[i], actualWords[i]);
        }
    }

    [SkippableTheory]
    // Standard prime: p = 17, threshold = primeModulus - b = 7 (for b = 10)
    [InlineData(6u, 10u, 17u)] // Just below threshold: (6 + 10 = 16 < 17) -> 16
    [InlineData(7u, 10u, 17u)] // Exactly at threshold: (7 + 10 = 17) -> 0
    [InlineData(8u, 10u, 17u)] // Just above threshold: (8 + 10 = 18 > 17) -> 1
    [InlineData(5u, 10u, 17u)] // Normal no-wrap
    [InlineData(12u, 10u, 17u)] // Normal wrap
    [InlineData(0u, 0u, 17u)] // Zero
    [InlineData(16u, 0u, 17u)] // Just below prime
    [InlineData(0u, 16u, 17u)] // Just below prime
    [InlineData(16u, 1u, 17u)] // Exactly prime (wraps to 0)
    [InlineData(16u, 16u, 17u)] // Max valid residues for p=17 -> 15
    // Near uint.MaxValue prime: p = 4294967295u, threshold = 4294967293u (for b = 2u)
    [InlineData(4294967292u, 2u, 4294967295u)] // Just below threshold -> 4294967294
    [InlineData(4294967293u, 2u, 4294967295u)] // Exactly at threshold -> 0
    [InlineData(4294967294u, 2u, 4294967295u)] // Just above threshold -> 1
    [InlineData(0u, 4294967294u, 4294967295u)]
    [InlineData(4294967294u, 4294967294u, 4294967295u)] // Max valid residues -> 4294967293
    [InlineData(4294967293u, 1u, 4294967295u)]
    [InlineData(4294967294u, 1u, 4294967295u)] // Wraps to 0
    public void Test_ModAdd_BoundaryConditions(uint a, uint b, uint p)
    {
        CudaCapability.EnsureGpuAvailable();
        var expected = (uint)(((ulong)a + b) % p);
        var actual = GpuBigIntNttKernels.ModAdd(a, b, p);
        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Mul_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();

        RunBinaryOpTest(
            engine,
            resultWords: TestSizeWords * 2,
            contextWords: TestSizeWords * 4,
            prepareData: null,
            cpuFunc: (a, b) => a * b,
            gpuFunc: (a, b) => a * b
        );
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Div_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        var sizeWords = 16;
        RunBinaryOpTest(
            engine,
            resultWords: sizeWords,
            contextWords: sizeWords * 8,
            prepareData: (arrA, arrB) =>
            {
                arrB[sizeWords - 1] |= 0x80000000;
            },
            cpuFunc: (a, b) => a / b,
            gpuFunc: (a, b) => a / b,
            inputWords: sizeWords
        );
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_ModuloOperator_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 3, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 62);
        var arrB = GenerateRandomArray(TestSizeWords, 63);
        arrB[TestSizeWords - 1] &= 0x7FFFFFFF;
        if (arrB[0] == 0) arrB[0] = 1;

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        gpuResult.Value = gpuA % gpuB;
        AssertGpuResultEqualsCpu(bigA % bigB, gpuResult);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Bitwise_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 22);
        var arrB = GenerateRandomArray(TestSizeWords, 23);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        // Test AND
        gpuResult.Value = gpuA & gpuB;
        Assert.Equal(arrA.Zip(arrB, (a, b) => a & b).ToArray(), gpuResult.ToHostArray());

        // Test OR
        gpuResult.Value = gpuA | gpuB;
        Assert.Equal(arrA.Zip(arrB, (a, b) => a | b).ToArray(), gpuResult.ToHostArray());

        // Test XOR
        gpuResult.Value = gpuA ^ gpuB;
        Assert.Equal(arrA.Zip(arrB, (a, b) => a ^ b).ToArray(), gpuResult.ToHostArray());

        // Test NOT
        gpuResult.Value = ~gpuA;
        Assert.Equal(arrA.Select(a => ~a).ToArray(), gpuResult.ToHostArray());
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot, 143)]
    [InlineData(GpuExecutionEngine.GSharpAot, 0)] // Edge case: zero shift
    [InlineData(GpuExecutionEngine.GSharpAot,
        1024)] // Edge case: shift >= total bits (TestSizeWords * 32 = 256*32 = 8192)
    [InlineData(GpuExecutionEngine.GSharpAot, 8192)] // Edge case: shift == total bits
    [InlineData(GpuExecutionEngine.GSharpAot, 8200)] // Edge case: shift > total bits
    [InlineData(GpuExecutionEngine.GSharpAot, 31)] // Edge case: intra-word boundary
    [InlineData(GpuExecutionEngine.GSharpAot, 32)] // Edge case: exact word boundary
    [InlineData(GpuExecutionEngine.LegacyNative, 143)]
    [InlineData(GpuExecutionEngine.LegacyNative, 0)]
    public void Test_ShiftLeft_Correctness(GpuExecutionEngine engine, int shiftBits)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 5);

        var bigA = ToBigInteger(arrA);
        var expectedBig = bigA << shiftBits;

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        gpuResult.Value = gpuA << shiftBits;

        AssertGpuResultEqualsCpu(expectedBig, gpuResult, TestSizeWords);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot, 143)]
    [InlineData(GpuExecutionEngine.GSharpAot, 0)] // Edge case: zero shift
    [InlineData(GpuExecutionEngine.GSharpAot, 1024)] // Edge case: shift >= total bits
    [InlineData(GpuExecutionEngine.GSharpAot, 8192)] // Edge case: shift == total bits
    [InlineData(GpuExecutionEngine.GSharpAot, 8200)] // Edge case: shift > total bits
    [InlineData(GpuExecutionEngine.GSharpAot, 31)] // Edge case: intra-word boundary
    [InlineData(GpuExecutionEngine.GSharpAot, 32)] // Edge case: exact word boundary
    [InlineData(GpuExecutionEngine.LegacyNative, 143)]
    [InlineData(GpuExecutionEngine.LegacyNative, 0)]
    public void Test_ShiftRight_Correctness(GpuExecutionEngine engine, int shiftBits)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 6);

        var bigA = ToBigInteger(arrA);
        var expectedBig = bigA >> shiftBits;

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        gpuResult.Value = gpuA >> shiftBits;

        AssertGpuResultEqualsCpu(expectedBig, gpuResult);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_ComparisonOperators_Correctness(GpuExecutionEngine engine)
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
    public void Test_ComparisonOperators_ReductionEdgeCases(GpuExecutionEngine engine)
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
    [Category(TestCategory.Syntax)]
    public void Test_Operator_IncrementDecrement(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var workspace = new GpuWorkspace(4, engine);

        // 1. Basic Happy Path (Increment & Decrement)
        var a = new GpuBigUInt(workspace, new uint[] { 10, 0, 0, 0 });
        try
        {
            a++;
            Assert.Equal(11u, a.ToHostArray()[0]);

            ++a;
            Assert.Equal(12u, a.ToHostArray()[0]);

            a--;
            Assert.Equal(11u, a.ToHostArray()[0]);

            --a;
            Assert.Equal(10u, a.ToHostArray()[0]);
        }
        finally
        {
            a.Dispose();
        }

        // 2. Carry Propagation Boundary (0xFFFFFFFF -> 0x00000000 with carry to higher word)
        var carryVal = new GpuBigUInt(workspace, new uint[] { 0xFFFFFFFF, 0x00000000, 0, 0 });
        try
        {
            ++carryVal;
            var carryResult = carryVal.ToHostArray();
            Assert.Equal(0u, carryResult[0]);
            Assert.Equal(1u, carryResult[1]);

            // 3. Borrow Propagation Boundary (0x00000000, 0x00000001 -> 0xFFFFFFFF, 0x00000000)
            --carryVal;
            var borrowResult = carryVal.ToHostArray();
            Assert.Equal(0xFFFFFFFFu, borrowResult[0]);
            Assert.Equal(0u, borrowResult[1]);
        }
        finally
        {
            carryVal.Dispose();
        }
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_DivRem_Mod_ModPow_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 3, engine);

        var arrA = GenerateRandomArray(TestSizeWords, 60);
        var arrB = GenerateRandomArray(TestSizeWords, 61);

        // ensure b is smaller but non-zero
        arrB[TestSizeWords - 1] &= 0x7FFFFFFF;
        if (arrB[0] == 0) arrB[0] = 1;

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);

        using var gpuQ = new GpuBigUInt(context, TestSizeWords);
        using var gpuR = new GpuBigUInt(context, TestSizeWords);

        // DivRem
        GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
        AssertGpuResultEqualsCpu(bigA / bigB, gpuQ);
        AssertGpuResultEqualsCpu(bigA % bigB, gpuR);

        // Mod
        using var gpuMod = new GpuBigUInt(context, TestSizeWords);
        GpuBigIntDivRem.Mod(gpuA, gpuB, gpuMod);
        AssertGpuResultEqualsCpu(bigA % bigB, gpuMod);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_DivRem_EdgeCases(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 3, engine);

        var arrA = new uint[TestSizeWords];
        var arrB = new uint[TestSizeWords];
        arrA[0] = 5;
        arrB[0] = 10;

        // Case 1: a < b
        using (var gpuA = new GpuBigUInt(context, arrA))
        using (var gpuB = new GpuBigUInt(context, arrB))
        using (var gpuQ = new GpuBigUInt(context, TestSizeWords))
        using (var gpuR = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
            AssertGpuResultEqualsCpu(0, gpuQ);
            AssertGpuResultEqualsCpu(5, gpuR);
        }

        // Case 2: a == b
        arrA[0] = 10;
        using (var gpuA = new GpuBigUInt(context, arrA))
        using (var gpuB = new GpuBigUInt(context, arrB))
        using (var gpuQ = new GpuBigUInt(context, TestSizeWords))
        using (var gpuR = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
            AssertGpuResultEqualsCpu(1, gpuQ);
            AssertGpuResultEqualsCpu(0, gpuR);
        }

        // Case 3: b is a power of 2
        arrA[0] = 100;
        arrB[0] = 16;
        using (var gpuA = new GpuBigUInt(context, arrA))
        using (var gpuB = new GpuBigUInt(context, arrB))
        using (var gpuQ = new GpuBigUInt(context, TestSizeWords))
        using (var gpuR = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
            AssertGpuResultEqualsCpu(6, gpuQ);
            AssertGpuResultEqualsCpu(4, gpuR);
        }

        // Case 4: a = 0
        arrA[0] = 0;
        using (var gpuA = new GpuBigUInt(context, arrA))
        using (var gpuB = new GpuBigUInt(context, arrB))
        using (var gpuQ = new GpuBigUInt(context, TestSizeWords))
        using (var gpuR = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
            AssertGpuResultEqualsCpu(0, gpuQ);
            AssertGpuResultEqualsCpu(0, gpuR);
        }

        // Case 5: Divide by zero (throws)
        // CUDA Sticky error prevention: intentionally crashing kernel causes CudaErrorLaunchFailed for other tests
        arrB[0] = 0;
        using (var gpuA = new GpuBigUInt(context, arrA))
        using (var gpuB = new GpuBigUInt(context, arrB))
        using (var gpuQ = new GpuBigUInt(context, TestSizeWords))
        using (var gpuR = new GpuBigUInt(context, TestSizeWords))
        {
            Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR));
        }
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Square_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 4, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 90);
        var bigA = ToBigInteger(arrA);

        using var gpuA = new GpuBigUInt(context, TestSizeWords * 4);
        using var tempA = new GpuBigUInt(context, arrA);
        gpuA.CopyFrom(tempA);
        gpuA.Square(TestSizeWords);
        var actual = ToBigInteger(gpuA.ToHostArray().Take(TestSizeWords * 2).ToArray());
        var expected = bigA * bigA;

        var mask = (BigInteger.One << (TestSizeWords * 2 * 32)) - 1;
        expected &= mask;

        Assert.Equal(expected, actual);
    }

    [SkippableFact]
    public void Test_HalfGcdReference_Correctness()
    {
        CudaCapability.EnsureGpuAvailable();
        var rand = new Random(42);
        for (int i = 0; i < 20; i++)
        {
            var bytesA = new byte[256];
            var bytesB = new byte[256];
            rand.NextBytes(bytesA);
            rand.NextBytes(bytesB);
            bytesA[255] = (byte)(bytesA[255] | 0x80); // Ensure large

            var a = new BigInteger(bytesA, isUnsigned: true, isBigEndian: true);
            var b = new BigInteger(bytesB, isUnsigned: true, isBigEndian: true);

            if (a < b)
            {
                var tmp = a;
                a = b;
                b = tmp;
            }

            var expectedGcd = BigInteger.GreatestCommonDivisor(a, b);
            var actualGcd = HalfGcdCpuReference.Gcd(a, b);

            Assert.Equal(expectedGcd, actualGcd);
        }
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Gcd_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);

        // Generate two random big integers
        var arrA = GenerateRandomArray(TestSizeWords, 50);
        var arrB = GenerateRandomArray(TestSizeWords, 51);

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);

        // Calculate expected GCD using System.Numerics.BigInteger
        var expectedGcd = BigInteger.GreatestCommonDivisor(bigA, bigB);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        GpuBigIntMath.Gcd(gpuA, gpuB, gpuResult);

        var actualGcd = ToBigInteger(gpuResult.ToHostArray());

        Assert.Equal(expectedGcd, actualGcd);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Gcd_EmptyCollection_ThrowsException(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(10, engine);
        var arrEmpty = Array.Empty<uint>();

        Assert.ThrowsAny<Exception>(() =>
        {
            using var gpuA = new GpuBigUInt(context, arrEmpty);
            using var gpuB = new GpuBigUInt(context, arrEmpty);
            using var gpuResult = new GpuBigUInt(context, 0);

            GpuBigIntMath.Gcd(gpuA, gpuB, gpuResult);
        });
    }

    [SkippableTheory]
    // Happy path
    [InlineData(GpuExecutionEngine.GSharpAot, 48u, 18u)]
    // Edge cases: 0
    [InlineData(GpuExecutionEngine.GSharpAot, 0u, 0u)]
    [InlineData(GpuExecutionEngine.GSharpAot, 0u, 10u)]
    [InlineData(GpuExecutionEngine.GSharpAot, 10u, 0u)]
    // Edge cases: 1
    [InlineData(GpuExecutionEngine.GSharpAot, 1u, 1u)]
    [InlineData(GpuExecutionEngine.GSharpAot, 1u, 100u)]
    [InlineData(GpuExecutionEngine.GSharpAot, 100u, 1u)]
    // Edge cases: MaxValue and Alternating Bits
    [InlineData(GpuExecutionEngine.GSharpAot, uint.MaxValue, uint.MaxValue)]
    [InlineData(GpuExecutionEngine.GSharpAot, uint.MaxValue, 1u)]
    [InlineData(GpuExecutionEngine.GSharpAot, 0x80000000u, 0x55555555u)]
    public void Test_Gcd_SingleElement_ReturnsCorrectGcd(GpuExecutionEngine engine, uint valA, uint valB)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(10, engine);
        var arrA = new uint[] { valA };
        var arrB = new uint[] { valB };

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);
        var expectedGcd = BigInteger.GreatestCommonDivisor(bigA, bigB);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuResult = new GpuBigUInt(context, 1);

        GpuBigIntMath.Gcd(gpuA, gpuB, gpuResult);

        var actualGcd = ToBigInteger(gpuResult.ToHostArray());
        Assert.Equal(expectedGcd, actualGcd);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Function_ModInverse_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);

        // Find a coprime pair (a, m)
        BigInteger bigA, bigM;
        uint[] arrA, arrM;
        do
        {
            arrA = GenerateRandomArray(TestSizeWords, 60);
            arrM = GenerateRandomArray(TestSizeWords, 61);
            // Ensure m > 1 and a > 0
            arrM[0] |= 3;
            arrA[0] |= 1;
            bigA = ToBigInteger(arrA);
            bigM = ToBigInteger(arrM);
        } while (BigInteger.GreatestCommonDivisor(bigA, bigM) != 1);

        var expectedInverse = ModInverseCpu(bigA, bigM);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuM = new GpuBigUInt(context, arrM);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        GpuBigIntMath.ModInverse(gpuA, gpuM, gpuResult);
        var actualInverseMath = ToBigInteger(gpuResult.ToHostArray());
        Assert.Equal(expectedInverse, actualInverseMath);

        using var gpuResult2 = gpuA.ModInverse(gpuM);
        var actualInverseStruct = ToBigInteger(gpuResult2.ToHostArray());
        Assert.Equal(expectedInverse, actualInverseStruct);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Function_ModInverse_NotCoprime_Throws(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);

        // Create a non-coprime pair (e.g., both even)
        var arrA = GenerateRandomArray(TestSizeWords, 62);
        var arrM = GenerateRandomArray(TestSizeWords, 63);
        arrA[0] &= ~1u; // Make even
        arrM[0] &= ~1u; // Make even
        arrA[0] |= 2u;
        arrM[0] |= 2u;

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuM = new GpuBigUInt(context, arrM);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        Assert.Throws<ArithmeticException>(() => GpuBigIntMath.ModInverse(gpuA, gpuM, gpuResult));
    }

    private static BigInteger ModInverseCpu(BigInteger a, BigInteger n)
    {
        BigInteger i = n, v = 0, d = 1;
        while (a > 0)
        {
            BigInteger t = i / a, x = a;
            a = i % x;
            i = x;
            x = d;
            d = v - t * x;
            v = x;
        }

        if (i != 1) throw new ArithmeticException("Not coprime.");
        v %= n;
        if (v < 0) v = (v + n) % n;
        return v;
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_CompareTo_Correctness(GpuExecutionEngine engine)
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
    public void Test_LengthProperties_Correctness(GpuExecutionEngine engine)
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
    public void Test_GetBitLength_AtomicStress(GpuExecutionEngine engine)
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
    public void Test_State_Correctness(GpuExecutionEngine engine)
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

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Sqrt_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        var sizeWords = 16;
        using var context = new GpuWorkspace(sizeWords * 8, engine);

        var arrA = GenerateRandomArray(sizeWords, 123);
        // Ensure A is fairly large so we can test convergence well
        arrA[sizeWords - 1] |= 0x40000000;

        var bigA = ToBigInteger(arrA);
        var expectedSqrt = bigA.Sqrt();

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuResult = new GpuBigUInt(context, sizeWords);

        gpuResult.Value = GpuBigUIntExpr.Sqrt(gpuA);

        var resultArr = gpuResult.ToHostArray();
        var actualBig = ToBigInteger(resultArr);

        Assert.Equal(expectedSqrt, actualBig);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Sqrt_EdgeCases(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 4, engine);
        var arrA = new uint[TestSizeWords];

        // Case 1: a = 0
        arrA[0] = 0;
        using (var gpuA = new GpuBigUInt(context, arrA))
        {
            using var gpuResult = new GpuBigUInt(context, TestSizeWords);
            gpuResult.Value = GpuBigUIntExpr.Sqrt(gpuA);
            AssertGpuResultEqualsCpu(0, gpuResult);
        }

        // Case 2: a = 1
        arrA[0] = 1;
        using (var gpuA = new GpuBigUInt(context, arrA))
        {
            using var gpuResult = new GpuBigUInt(context, TestSizeWords);
            gpuResult.Value = GpuBigUIntExpr.Sqrt(gpuA);
            AssertGpuResultEqualsCpu(1, gpuResult);
        }

        // Case 3: a = 16 (Power of 2)
        arrA[0] = 16;
        using (var gpuA = new GpuBigUInt(context, arrA))
        {
            using var gpuResult = new GpuBigUInt(context, TestSizeWords);
            gpuResult.Value = GpuBigUIntExpr.Sqrt(gpuA);
            AssertGpuResultEqualsCpu(4, gpuResult);
        }
    }


    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_ModPow_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 13, engine);

        var arrBase = GenerateRandomArray(TestSizeWords, 800);
        var arrExp = GenerateRandomArray(1, 801); // Very small exponent for testing speed
        var arrMod = GenerateRandomArray(TestSizeWords, 802);
        arrMod[TestSizeWords - 1] &= 0x7FFFFFFF;
        if (arrMod[0] == 0) arrMod[0] = 1;

        var bigBase = ToBigInteger(arrBase);
        var bigExp = ToBigInteger(arrExp);
        var bigMod = ToBigInteger(arrMod);

        var expected = BigInteger.ModPow(bigBase, bigExp, bigMod);

        using var gpuBase = new GpuBigUInt(context, arrBase);
        var arrExpPadded = new uint[TestSizeWords];
        Array.Copy(arrExp, arrExpPadded, arrExp.Length);
        using var gpuExp = new GpuBigUInt(context, arrExpPadded);
        using var gpuMod = new GpuBigUInt(context, arrMod);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        var actual = ToBigInteger(gpuResult.ToHostArray());

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_ModPow_EdgeCases(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 13, engine);
        var arrBase = new uint[TestSizeWords];
        var arrExp = new uint[TestSizeWords];
        var arrMod = new uint[TestSizeWords];

        // Case 1: Mod = 1
        arrBase[0] = 5;
        arrExp[0] = 3;
        arrMod[0] = 1;
        using (var gpuBase = new GpuBigUInt(context, arrBase))
        using (var gpuExp = new GpuBigUInt(context, arrExp))
        using (var gpuMod = new GpuBigUInt(context, arrMod))
        using (var gpuResult = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
            AssertGpuResultEqualsCpu(0, gpuResult);
        }

        // Case 2: Base = 0, Exp > 0
        arrBase[0] = 0;
        arrExp[0] = 5;
        arrMod[0] = 7;
        using (var gpuBase = new GpuBigUInt(context, arrBase))
        using (var gpuExp = new GpuBigUInt(context, arrExp))
        using (var gpuMod = new GpuBigUInt(context, arrMod))
        using (var gpuResult = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
            AssertGpuResultEqualsCpu(0, gpuResult);
        }

        // Case 3: Exp = 0
        arrBase[0] = 5;
        arrExp[0] = 0;
        arrMod[0] = 7;
        using (var gpuBase = new GpuBigUInt(context, arrBase))
        using (var gpuExp = new GpuBigUInt(context, arrExp))
        using (var gpuMod = new GpuBigUInt(context, arrMod))
        using (var gpuResult = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
            AssertGpuResultEqualsCpu(1, gpuResult);
        }

        // Case 4: Divide by zero (Mod = 0)
        // CUDA Sticky error prevention: intentionally crashing kernel causes CudaErrorLaunchFailed for other tests
        arrMod[0] = 0;
        using (var gpuBase = new GpuBigUInt(context, arrBase))
        using (var gpuExp = new GpuBigUInt(context, arrExp))
        using (var gpuMod = new GpuBigUInt(context, arrMod))
        using (var gpuResult = new GpuBigUInt(context, TestSizeWords))
        {
            Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult));
        }
    }


    private void AssertGpuResultEqualsCpu(BigInteger expected, GpuBigUInt actualGpu, int truncateWords = -1)
    {
        if (truncateWords > 0)
        {
            var mask = (BigInteger.One << (truncateWords * 32)) - 1;
            expected &= mask;
        }

        var actualBig = ToBigInteger(actualGpu.ToHostArray());
        if (expected != actualBig)
        {
            Assert.Equal(expected, actualBig);
        }
    }

    private void RunBinaryOpTest(
        GpuExecutionEngine engine,
        int resultWords,
        int contextWords,
        Action<uint[], uint[]> prepareData,
        Func<BigInteger, BigInteger, BigInteger> cpuFunc,
        Func<GpuBigUInt, GpuBigUInt, GpuBigUIntExpr> gpuFunc,
        int inputWords = -1)
    {
        var size = inputWords > 0 ? inputWords : TestSizeWords;
        using var context = new GpuWorkspace(contextWords, engine);
        var arrA = GenerateRandomArray(size, 100);
        var arrB = GenerateRandomArray(size, 101);

        prepareData?.Invoke(arrA, arrB);

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);
        var expected = cpuFunc(bigA, bigB);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);
        using var gpuResult = new GpuBigUInt(context, resultWords);

        gpuResult.Value = gpuFunc(gpuA, gpuB);

        AssertGpuResultEqualsCpu(expected, gpuResult, resultWords);
    }

    [SkippableTheory]
    [InlineData(0u, 0u, 4253024257u, 0u)]
    [InlineData(100u, 200u, 4253024257u, 300u)]
    [InlineData(4253024256u, 1u, 4253024257u, 0u)]
    [InlineData(4253024256u, 2u, 4253024257u, 1u)]
    [InlineData(0x80000000u, 0x80000000u, 4253024257u, 41943039u)] // 2^32 % 4253024257 = 41943039
    [InlineData(4293918720u, 4293918720u, 4293918721u, 4293918719u)] // Near uint.MaxValue prime
    [InlineData(0xC0000000u, 0xC0000000u, 4293918721u, 2148532223u)] // 6442450944 - 4293918721 = 2148532223
    public void Test_ModAdd_OverflowSafe(uint a, uint b, uint prime, uint expected)
    {
        CudaCapability.EnsureGpuAvailable();
        var actual = GpuBigIntNttKernels.ModAdd(a, b, prime);
        Assert.Equal(expected, actual);
    }

    [SkippableFact]
    public void Test_NttCarryResolver_ZeroLengthBuffers_ReturnsEarly()
    {
        CudaCapability.EnsureGpuAvailable();
        var low = Array.Empty<uint>();
        var high = Array.Empty<uint>();
        var output = Array.Empty<uint>();
        var blockCarries = Array.Empty<uint>();

        // Assert no exceptions thrown for 0 length buffers due to guard clause
        var exception = Record.Exception(() =>
        {
            GpuBigIntNttKernels.LaunchResolveNttCarriesBlockedKernel(low, high, output, blockCarries, 0,
                0, 256);
            GpuBigIntNttKernels.LaunchResolveNttCarriesApplyKernel(output, blockCarries, 0, 256, 0);
        });
        Assert.Null(exception);
    }

    [SkippableFact]
    public void Test_NttCarryResolver_SingleWord_ValidatesCarryCalculation()
    {
        CudaCapability.EnsureGpuAvailable();
        var low = new uint[] { 0x12345678, 0x9ABCDEF0 };
        var high = new uint[] { 0, 0 };
        var output = new uint[1];
        var blockCarries = new uint[1];

        GpuBigIntNttKernels.LaunchResolveNttCarriesBlockedKernel(low, high, output, blockCarries, 2, 1,
            256);
        GpuBigIntNttKernels.LaunchResolveNttCarriesApplyKernel(output, blockCarries, 1, 256, 1);

        // Calculation logic:
        // sum0 = 0x12345678 -> low16 = 0x5678, carry = 0x1234
        // sum1 = 0x1234 + 0x9ABCDEF0 = 0x9ABCF124 -> high16 = 0xF124, carry = 0x9ABC
        // output[0] = low16 | (high16 << 16) = 0x5678 | (0xF124 << 16) = 0xF1245678
        Assert.Equal(0xF1245678, output[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Stress)]
    public void Test_GpuBigInt_ConcurrentAccess_DataRaceSafety()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        var engine = GpuExecutionEngine.GSharpAot;
        using var context = new GpuWorkspace(1024, engine);

        var arrA = GenerateRandomArray(256, 42);
        var arrB = GenerateRandomArray(256, 43);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);

        // Compute expected single-threaded baseline result
        using var expectedGpu = new GpuBigUInt(context, 256);
        expectedGpu.Value = gpuA + gpuB;
        var expectedHostResult = expectedGpu.ToHostArray();

        // Execute 100 concurrent Add operations modifying different targets
        // but reading from the same gpuA / gpuB to ensure context/stream stability
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 100, i =>
        {
            try
            {
                // Ensure each worker thread has the CUDA context bound
                using var threadScope = new GpuKernelLoader.CudaContextScope();

                using var gpuResult = new GpuBigUInt(context, 256);
                gpuResult.Value = gpuA + gpuB;

                var hostResult = gpuResult.ToHostArray();
                Assert.Equal(256, hostResult.Length);
                Assert.Equal(expectedHostResult, hostResult);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }
}










