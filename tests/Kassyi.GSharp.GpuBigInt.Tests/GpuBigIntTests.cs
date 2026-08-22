using System;
using System.Linq;
using System.Numerics;
using Xunit;

namespace Kassyi.GSharp.GpuBigInt.Tests
{
    public class GpuBigIntTests : IDisposable
    {
        private readonly GpuContext _context;
        private const int TestSizeWords = 1024; // 32768 bits

        public GpuBigIntTests()
        {
            // MaxWords enough to hold our tests
            _context = new GpuContext(TestSizeWords * 2);
        }

        public void Dispose()
        {
            _context.Dispose();
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

        [Fact]
        public void Test_Add_Correctness()
        {
            var arrA = GenerateRandomArray(TestSizeWords, 1);
            var arrB = GenerateRandomArray(TestSizeWords, 2);

            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);
            var expectedBig = bigA + bigB;

            using var gpuA = new GpuBigInteger(_context, arrA);
            using var gpuB = new GpuBigInteger(_context, arrB);
            using var gpuResult = new GpuBigInteger(_context, TestSizeWords);

            GpuBigInteger.Add(gpuA, gpuB, gpuResult);

            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            // Note: Since we are adding two TestSizeWords, a carry out might be lost if we don't allocate TestSizeWords+1.
            // Our kernel drops the final carry if it exceeds numWords. 
            // So we apply a bitmask to expectedBig equivalent to TestSizeWords words.
            var mask = (BigInteger.One << (TestSizeWords * 32)) - 1;
            expectedBig &= mask;

            Assert.Equal(expectedBig, actualBig);
        }

        [Fact]
        public void Test_Sub_Correctness()
        {
            var arrA = GenerateRandomArray(TestSizeWords, 3);
            var arrB = GenerateRandomArray(TestSizeWords, 4);

            // Ensure A >= B to avoid negative wrap-around complexities in BigInteger comparison
            // For simplicity, we just force the highest word of A to be larger.
            arrA[TestSizeWords - 1] |= 0x40000000;
            arrB[TestSizeWords - 1] &= 0x3FFFFFFF;

            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);
            var expectedBig = bigA - bigB;

            using var gpuA = new GpuBigInteger(_context, arrA);
            using var gpuB = new GpuBigInteger(_context, arrB);
            using var gpuResult = new GpuBigInteger(_context, TestSizeWords);

            GpuBigInteger.Sub(gpuA, gpuB, gpuResult);

            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            Assert.Equal(expectedBig, actualBig);
        }

        [Fact]
        public void Test_ShiftLeft_Correctness()
        {
            var arrA = GenerateRandomArray(TestSizeWords, 5);
            int shiftBits = 143; // Random shift

            var bigA = ToBigInteger(arrA);
            var expectedBig = bigA << shiftBits;

            using var gpuA = new GpuBigInteger(_context, arrA);
            using var gpuResult = new GpuBigInteger(_context, TestSizeWords);

            GpuBigInteger.ShiftLeft(gpuA, shiftBits, gpuResult);

            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            var mask = (BigInteger.One << (TestSizeWords * 32)) - 1;
            expectedBig &= mask;

            Assert.Equal(expectedBig, actualBig);
        }

        [Fact]
        public void Test_ShiftRight_Correctness()
        {
            var arrA = GenerateRandomArray(TestSizeWords, 6);
            int shiftBits = 143; // Random shift

            var bigA = ToBigInteger(arrA);
            var expectedBig = bigA >> shiftBits;

            using var gpuA = new GpuBigInteger(_context, arrA);
            using var gpuResult = new GpuBigInteger(_context, TestSizeWords);

            GpuBigInteger.ShiftRight(gpuA, shiftBits, gpuResult);

            var resultArr = gpuResult.ToHostArray();
            var actualBig = ToBigInteger(resultArr);

            Assert.Equal(expectedBig, actualBig);
        }
        [Fact]
        public void Test_ExpressionTree_Correctness()
        {
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

            using var gpuA = new GpuBigInteger(_context, arrA);
            using var gpuB = new GpuBigInteger(_context, arrB);
            using var gpuC = new GpuBigInteger(_context, arrC);
            using var gpuResult = new GpuBigInteger(_context, TestSizeWords);

            // Magic Expression Tree evaluation
            gpuResult.Value = (((gpuA + gpuB) << shiftBits) - gpuC) & gpuA;

            var actualBig = ToBigInteger(gpuResult.ToHostArray());

            Assert.Equal(expectedBig, actualBig);
        }
    }
}
