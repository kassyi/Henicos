namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Shared test helpers, constants, and CPU oracle methods for GpuBigInt core tests.
/// </summary>
internal static class GpuBigIntCoreTestHelpers
{
    public const int TestSizeWords = 256; // 8192 bits

    public static uint[] GenerateRandomArray(int numWords, int seed = 42)
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

    public static BigInteger ToBigInteger(uint[] words)
    {
        var bytes = new byte[words.Length * 4 + 1]; // +1 for positive sign guarantee
        Buffer.BlockCopy(words, 0, bytes, 0, words.Length * 4);
        return new BigInteger(bytes);
    }

    public static uint[] FromBigInteger(BigInteger b, int targetWords)
    {
        var bytes = b.ToByteArray();
        var words = new uint[targetWords];
        int copyBytes = Math.Min(bytes.Length, targetWords * 4);
        Buffer.BlockCopy(bytes, 0, words, 0, copyBytes);
        return words;
    }

    public static BigInteger WordsToBigInteger(uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    public static void AssertGpuResultEqualsCpu(BigInteger expected, GpuBigUInt actualGpu, int truncateWords = -1)
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

    public static void RunBinaryOpTest(
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

    public static BigInteger ModInverseCpu(BigInteger a, BigInteger n)
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
}
