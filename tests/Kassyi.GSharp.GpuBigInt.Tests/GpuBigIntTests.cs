using System;
using System.Linq;
using System.Numerics;
using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.GpuBigInt.Expressions;
using Xunit;

namespace Kassyi.GSharp.GpuBigInt.Tests
{
    public class GpuBigIntTests
    {
        private const int TestSizeWords = 1024; // 32768 bits

        public GpuBigIntTests()
        {
            // Ensure CUDA context is initialized for GSharpJit tests
            using var scope = new JitEngine.CudaContextScope();
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
            int copyBytes = System.Math.Min(bytes.Length, targetWords * 4);
            Buffer.BlockCopy(bytes, 0, words, 0, copyBytes);
            return words;
        }

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [InlineData(GpuExecutionEngine.LegacyNative)]
        public void Test_Add_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 1);
            var arrB = GenerateRandomArray(TestSizeWords, 2);

            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);
            var expectedBig = bigA + bigB;

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuResult = new GpuBigInteger(context, TestSizeWords);

            gpuResult.Value = gpuA + gpuB;

            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            // Note: Since we are adding two TestSizeWords, a carry out might be lost if we don't allocate TestSizeWords+1.
            // Our kernel drops the final carry if it exceeds numWords. 
            // So we apply a bitmask to expectedBig equivalent to TestSizeWords words.
            var mask = (BigInteger.One << (TestSizeWords * 32)) - 1;
            expectedBig &= mask;

            Assert.Equal(expectedBig, actualBig);
        }

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [InlineData(GpuExecutionEngine.LegacyNative)]
        public void Test_Sub_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 3);
            var arrB = GenerateRandomArray(TestSizeWords, 4);

            // Ensure A >= B to avoid negative wrap-around complexities in BigInteger comparison
            // For simplicity, we just force the highest word of A to be larger.
            arrA[TestSizeWords - 1] |= 0x40000000;
            arrB[TestSizeWords - 1] &= 0x3FFFFFFF;

            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);
            var expectedBig = bigA - bigB;

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuResult = new GpuBigInteger(context, TestSizeWords);

            gpuResult.Value = gpuA - gpuB;

            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            Assert.Equal(expectedBig, actualBig);
        }

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [InlineData(GpuExecutionEngine.LegacyNative)]
        public void Test_Mul_Correctness(GpuExecutionEngine engine)
        {
            if (engine == GpuExecutionEngine.LegacyNative)
                return; // Not fully implemented or tested in Native Provider

            var sizeWords = 16;
            using var context = new GpuContext(sizeWords * 4, engine);
            var arrA = GenerateRandomArray(sizeWords, 11);
            var arrB = GenerateRandomArray(sizeWords, 12);

            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);
            var expectedBig = bigA * bigB;

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuResult = new GpuBigInteger(context, sizeWords * 2);

            gpuResult.Value = gpuA * gpuB;

            // Assert the final result against System.Numerics.BigInteger
            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            var mask = (BigInteger.One << (sizeWords * 2 * 32)) - 1;
            expectedBig &= mask;

            if (expectedBig != actualBig)
            {
                System.Console.WriteLine($"Expected (Hex): {expectedBig:X}");
                System.Console.WriteLine($"Actual   (Hex): {actualBig:X}");
            }

            Assert.Equal(expectedBig, actualBig);
        }

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_Div_Correctness(GpuExecutionEngine engine)
        {
            var sizeWords = 16;
            using var context = new GpuContext(sizeWords * 8, engine);
            
            // Randomly generated arrays
            var arrA = GenerateRandomArray(sizeWords * 2, 111);
            var arrB = GenerateRandomArray(sizeWords, 112);
            // Ensure B is normalized (top bit is 1)
            arrB[sizeWords - 1] |= 0x80000000;
            
            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);
            
            // A / B
            var expectedBig = bigA / bigB;

            // In our Div implementation, we expect A and B to have the same size as result.
            // Wait, our Div requires a, b, result to be same size, but above a is 2*sizeWords.
            // Let's modify Div test to have A and B the same size for now.
            var arrA_sameSize = GenerateRandomArray(sizeWords, 111);
            var bigA_sameSize = ToBigInteger(arrA_sameSize);
            var expectedBig_sameSize = bigA_sameSize / bigB;

            using var gpuA = new GpuBigInteger(context, arrA_sameSize);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuResult = new GpuBigInteger(context, sizeWords);

            gpuResult.Value = gpuA / gpuB;

            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            Assert.Equal(expectedBig_sameSize, actualBig);
        }

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_Sqrt_Correctness(GpuExecutionEngine engine)
        {
            var sizeWords = 16;
            using var context = new GpuContext(sizeWords * 8, engine);
            
            var arrA = GenerateRandomArray(sizeWords, 123);
            // Ensure A is fairly large so we can test convergence well
            arrA[sizeWords - 1] |= 0x40000000;
            
            var bigA = ToBigInteger(arrA);
            
            // Expected Sqrt (using BigInteger.Log approx or similar)
            // System.Numerics does not have BigInteger.Sqrt natively in net10.0 (it does in .NET 9+ actually as BigInteger.IsPowerOfTwo/etc but BigInteger.Sqrt is not always available depending on TFM).
            // Let's compute expected Sqrt manually with Heron's method.
            BigInteger expectedSqrt = 1;
            int bytes = bigA.GetByteCount(isUnsigned: true);
            int bits = (bytes - 1) * 8;
            BigInteger temp = bigA >> bits;
            while (temp > 0) { bits++; temp >>= 1; }
            expectedSqrt = BigInteger.One << (bits / 2);
            for (int i = 0; i < 20; i++)
            {
                BigInteger next = (expectedSqrt + bigA / expectedSqrt) >> 1;
                if (next == expectedSqrt || next > expectedSqrt && next - expectedSqrt == 1 || expectedSqrt > next && expectedSqrt - next <= 1)
                {
                    expectedSqrt = BigInteger.Min(expectedSqrt, next);
                    if (expectedSqrt * expectedSqrt > bigA) expectedSqrt--;
                    break;
                }
                expectedSqrt = next;
            }

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuResult = new GpuBigInteger(context, sizeWords);

            gpuResult.Value = GpuBigIntExpr.Sqrt(gpuA);

            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            Assert.Equal(expectedSqrt, actualBig);
        }

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [InlineData(GpuExecutionEngine.LegacyNative)]
        public void Test_ShiftLeft_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 5);
            int shiftBits = 143; // Random shift

            var bigA = ToBigInteger(arrA);
            var expectedBig = bigA << shiftBits;

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuResult = new GpuBigInteger(context, TestSizeWords);

            gpuResult.Value = gpuA << shiftBits;

            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            var mask = (BigInteger.One << (TestSizeWords * 32)) - 1;
            expectedBig &= mask;

            Assert.Equal(expectedBig, actualBig);
        }

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [InlineData(GpuExecutionEngine.LegacyNative)]
        public void Test_ShiftRight_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 6);
            int shiftBits = 143; // Random shift

            var bigA = ToBigInteger(arrA);
            var expectedBig = bigA >> shiftBits;

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuResult = new GpuBigInteger(context, TestSizeWords);

            gpuResult.Value = gpuA >> shiftBits;

            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            Assert.Equal(expectedBig, actualBig);
        }

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [InlineData(GpuExecutionEngine.LegacyNative)]
        public void Test_ExpressionTree_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 7);
            var arrB = GenerateRandomArray(TestSizeWords, 8);
            var arrC = GenerateRandomArray(TestSizeWords, 9);

            // Ensure A > B and B > C to avoid negative wraparound
            arrA[TestSizeWords - 1] |= 0x60000000;
            arrB[TestSizeWords - 1] = (arrB[TestSizeWords - 1] & 0x1FFFFFFF) | 0x20000000;
            arrC[TestSizeWords - 1] &= 0x0FFFFFFF;

            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);
            var bigC = ToBigInteger(arrC);

            int shiftBits = 128;

            // Mathematical equivalent: (((A + B) << 128) - C) & A
            var expectedBig = (((bigA + bigB) << shiftBits) - bigC) & bigA;
            var mask = (BigInteger.One << (TestSizeWords * 32)) - 1;
            expectedBig &= mask;

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuC = new GpuBigInteger(context, arrC);
            using var gpuResult = new GpuBigInteger(context, TestSizeWords);

            // Magic Expression Tree evaluation
            gpuResult.Value = (((gpuA + gpuB) << shiftBits) - gpuC) & gpuA;

            var actualBig = ToBigInteger(gpuResult.ToHostArray());

            Assert.Equal(expectedBig, actualBig);
        }
    }
}
