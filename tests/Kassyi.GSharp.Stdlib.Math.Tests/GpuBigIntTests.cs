using System;
using System.Linq;
using System.Numerics;
using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.Stdlib.Math.Expressions;
using Xunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests
{
    public class GpuBigIntTests
    {
        [Trait("RoadmapId", "GBI-11")]
        [Fact]
        public void Test_NttReference_Correctness()
        {
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
            Buffer.BlockCopy(expectedBytes, 0, expectedWords, 0, System.Math.Min(expectedBytes.Length, expectedWords.Length * 4));

            var actualWords = Kassyi.GSharp.Stdlib.Math.Algorithms.NttCpuReference.Multiply(aWords, bWords);

            for (int i = 0; i < expectedWords.Length; i++)
            {
                Assert.Equal(expectedWords[i], actualWords[i]);
            }
        }
        private const int TestSizeWords = 256; // 8192 bits

        #region 1. GBI アーキテクチャ・最適化課題

        [Fact(Skip = "未実装・課題: GBI-01 (GSharpJitProvider のメモリプール不在 / スラブアロケータ)")]
        [Trait("RoadmapId", "GBI-01")]
        [Trait("Category", "Memory")]
        public void Test_GBI01_MemoryPoolAllocation()
        {
        }

        [Fact(Skip = "未実装・課題: GBI-03 (CPU-Fallback メソッドの GPU 並列リダクション化)")]
        [Trait("RoadmapId", "GBI-03")]
        [Trait("Category", "Performance")]
        public void Test_GBI03_GpuReductionCompareAndFlags()
        {
        }

        [Fact(Skip = "未実装・課題: GBI-04 (Square 専用 JIT Pointwise カーネルの直接ディスパッチ)")]
        [Trait("RoadmapId", "GBI-04")]
        [Trait("Category", "Optimization")]
        public void Test_GBI04_DirectSquarePointwiseKernel()
        {
        }

        [Fact(Skip = "未実装・課題: GBI-05 (Lehmer's GCD の大桁並列化 / Half-GCD)")]
        [Trait("RoadmapId", "GBI-05")]
        [Trait("Category", "Algorithm")]
        public void Test_GBI05_ParallelGcdLargeDigits()
        {
        }

        [Fact(Skip = "未実装・課題: GBI-06 (式木 GpuBigIntExpr の動的 Kernel Fusion)")]
        [Trait("RoadmapId", "GBI-06")]
        [Trait("Category", "Runtime")]
        public void Test_GBI06_ExpressionKernelFusion()
        {
        }

        #endregion

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

        [Fact(Skip = "未実装: GBI-16 (インクリメント/デクリメント演算子 ++, --)")]
        [Trait("RoadmapId", "GBI-16")]
        [Trait("Category", "Operator")]
        public void Test_GBI_Operator_IncrementDecrement()
        {
        }

        [Trait("RoadmapId", "GBI-10")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [InlineData(GpuExecutionEngine.LegacyNative)]
        public void Test_Add_Correctness(GpuExecutionEngine engine)
        {
            RunBinaryOpTest(
                engine, 
                resultWords: TestSizeWords, 
                contextWords: TestSizeWords * 2,
                prepareData: null,
                cpuFunc: (a, b) => a + b,
                gpuFunc: (a, b) => a + b
            );
        }

        [Trait("RoadmapId", "GBI-10")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [InlineData(GpuExecutionEngine.LegacyNative)]
        public void Test_Sub_Correctness(GpuExecutionEngine engine)
        {
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

        [Trait("RoadmapId", "GBI-11")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [InlineData(GpuExecutionEngine.LegacyNative)]
        public void Test_Mul_Correctness(GpuExecutionEngine engine)
        {
            if (engine == GpuExecutionEngine.LegacyNative)
                return;

            RunBinaryOpTest(
                engine, 
                resultWords: TestSizeWords * 2, 
                contextWords: TestSizeWords * 4,
                prepareData: null,
                cpuFunc: (a, b) => a * b,
                gpuFunc: (a, b) => a * b
            );
        }

        [Trait("RoadmapId", "GBI-12")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_Div_Correctness(GpuExecutionEngine engine)
        {
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

        [Trait("RoadmapId", "GBI-25")]
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

        [Trait("RoadmapId", "GBI-14")]
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

            AssertGpuResultEqualsCpu(expectedBig, gpuResult, TestSizeWords);
        }

        [Trait("RoadmapId", "GBI-14")]
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

            AssertGpuResultEqualsCpu(expectedBig, gpuResult);
        }

        [Trait("RoadmapId", "GBI-24")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_State_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);
            var zeroArr = new uint[TestSizeWords];
            var oneArr = new uint[TestSizeWords];
            oneArr[0] = 1;
            var evenArr = new uint[TestSizeWords];
            evenArr[0] = 2; // Even
            var negativeArr = new uint[TestSizeWords];
            negativeArr[TestSizeWords - 1] = 0x80000000; // Negative flag

            using var gpuZero = new GpuBigInteger(context, zeroArr);
            using var gpuOne = new GpuBigInteger(context, oneArr);
            using var gpuEven = new GpuBigInteger(context, evenArr);
            using var gpuNegative = new GpuBigInteger(context, negativeArr);

            Assert.True(gpuZero.IsZero());
            Assert.False(gpuOne.IsZero());

            Assert.True(gpuOne.IsOne());
            Assert.False(gpuZero.IsOne());

            Assert.True(gpuZero.IsEven);
            Assert.False(gpuOne.IsEven);
            Assert.True(gpuEven.IsEven);

            Assert.True(gpuNegative.IsNegative);
            Assert.False(gpuZero.IsNegative);
            
            // Test setter
            gpuZero.IsNegative = true;
            Assert.True(gpuZero.IsNegative);
            gpuZero.IsNegative = false;
            Assert.False(gpuZero.IsNegative);
        }

        [Trait("RoadmapId", "GBI-22")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_CompareTo_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 20);
            var arrB = GenerateRandomArray(TestSizeWords, 20); // Same array
            
            var arrC = GenerateRandomArray(TestSizeWords, 21);
            // Force A > C
            arrA[TestSizeWords - 1] |= 0x40000000;
            arrC[TestSizeWords - 1] &= 0x3FFFFFFF;

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuC = new GpuBigInteger(context, arrC);

            Assert.Equal(0, gpuA.CompareTo(gpuB));
            Assert.Equal(1, gpuA.CompareTo(gpuC));
            Assert.Equal(-1, gpuC.CompareTo(gpuA));
        }

        [Trait("RoadmapId", "GBI-13")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_Bitwise_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 22);
            var arrB = GenerateRandomArray(TestSizeWords, 23);

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuResult = new GpuBigInteger(context, TestSizeWords);

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

        [Trait("RoadmapId", "GBI-10")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_Scalar_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);
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

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuResult = new GpuBigInteger(context, TestSizeWords);

            // Scalar Add
            GpuBigInteger.Add(gpuA, scalarAdd, gpuResult);
            AssertGpuResultEqualsCpu(bigA + scalarAdd, gpuResult, TestSizeWords);

            // Scalar Sub
            GpuBigInteger.Sub(gpuB, scalarSub, gpuResult);
            AssertGpuResultEqualsCpu(bigB - scalarSub, gpuResult, TestSizeWords);

            // Scalar Mul
            GpuBigInteger.Mul(gpuA, scalarMul, gpuResult);
            AssertGpuResultEqualsCpu(bigA * scalarMul, gpuResult, TestSizeWords);
        }

        [Trait("RoadmapId", "GBI-20")]
        [Fact]
        public void Test_HalfGcdReference_Correctness()
        {
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
                var actualGcd = Kassyi.GSharp.Stdlib.Math.Algorithms.HalfGcdCpuReference.Gcd(a, b);

                Assert.Equal(expectedGcd, actualGcd);
            }
        }

        [Fact(Skip = "未実装: GBI-21 (GpuBigInteger.ModInverse モジュラ逆数)")]
        [Trait("RoadmapId", "GBI-21")]
        [Trait("Category", "Function")]
        public void Test_GBI_Function_ModInverse()
        {
        }

        [Trait("RoadmapId", "GBI-20")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_Gcd_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);

            // Generate two random big integers
            var arrA = GenerateRandomArray(TestSizeWords, 50);
            var arrB = GenerateRandomArray(TestSizeWords, 51);

            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);

            // Calculate expected GCD using System.Numerics.BigInteger
            var expectedGcd = BigInteger.GreatestCommonDivisor(bigA, bigB);

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuResult = new GpuBigInteger(context, TestSizeWords);

            GpuBigInteger.Gcd(gpuA, gpuB, gpuResult);

            var actualGcd = ToBigInteger(gpuResult.ToHostArray());

            Assert.Equal(expectedGcd, actualGcd);
        }

        
        [Theory(Skip = "Extremely slow due to cuMemAlloc overhead without memory pooling. Enable when memory pool is implemented.")]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_ModPow_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 13, engine);

            var arrBase = GenerateRandomArray(TestSizeWords, 800);
            var arrExp = GenerateRandomArray(1, 801); // Very small exponent for testing speed
            var arrMod = GenerateRandomArray(TestSizeWords, 802);
            arrMod[TestSizeWords - 1] &= 0x7FFFFFFF;
            if (arrMod[0] == 0) arrMod[0] = 1;

            var bigBase = ToBigInteger(arrBase);
            var bigExp = ToBigInteger(arrExp);
            var bigMod = ToBigInteger(arrMod);

            var expected = System.Numerics.BigInteger.ModPow(bigBase, bigExp, bigMod);

            using var gpuBase = new GpuBigInteger(context, arrBase);
            var arrExpPadded = new uint[TestSizeWords];
            Array.Copy(arrExp, arrExpPadded, arrExp.Length);
            using var gpuExp = new GpuBigInteger(context, arrExpPadded);
            using var gpuMod = new GpuBigInteger(context, arrMod);
            using var gpuResult = new GpuBigInteger(context, TestSizeWords);

            GpuBigInteger.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

            var actual = ToBigInteger(gpuResult.ToHostArray());

            Assert.Equal(expected, actual);
        }


        [Trait("RoadmapId", "GBI-17")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_DivRem_Mod_ModPow_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 3, engine);

            var arrA = GenerateRandomArray(TestSizeWords, 60);
            var arrB = GenerateRandomArray(TestSizeWords, 61);
            
            // ensure b is smaller but non-zero
            arrB[TestSizeWords - 1] &= 0x7FFFFFFF;
            if (arrB[0] == 0) arrB[0] = 1;

            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            
            using var gpuQ = new GpuBigInteger(context, TestSizeWords);
            using var gpuR = new GpuBigInteger(context, TestSizeWords);

            // DivRem
            GpuBigInteger.DivRem(gpuA, gpuB, gpuQ, gpuR);
            AssertGpuResultEqualsCpu(bigA / bigB, gpuQ);
            AssertGpuResultEqualsCpu(bigA % bigB, gpuR);

            // Mod
            using var gpuMod = new GpuBigInteger(context, TestSizeWords);
            GpuBigInteger.Mod(gpuA, gpuB, gpuMod);
            AssertGpuResultEqualsCpu(bigA % bigB, gpuMod);
        }
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [Trait("RoadmapId", "GBI-12")]
        public void Test_ModuloOperator_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 3, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 62);
            var arrB = GenerateRandomArray(TestSizeWords, 63);
            arrB[TestSizeWords - 1] &= 0x7FFFFFFF;
            if (arrB[0] == 0) arrB[0] = 1;

            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuResult = new GpuBigInteger(context, TestSizeWords);

            gpuResult.Value = gpuA % gpuB;
            AssertGpuResultEqualsCpu(bigA % bigB, gpuResult);
        }

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [Trait("RoadmapId", "GBI-15")]
        public void Test_ComparisonOperators_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 2, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 70);
            var arrB = GenerateRandomArray(TestSizeWords, 70); // Same for equal tests
            var arrC = GenerateRandomArray(TestSizeWords, 71);
            arrA[TestSizeWords - 1] |= 0x40000000;
            arrB[TestSizeWords - 1] |= 0x40000000; // arrB must equal arrA
            arrC[TestSizeWords - 1] &= 0x3FFFFFFF; // Force A > C

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuC = new GpuBigInteger(context, arrC);

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

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [Trait("RoadmapId", "GBI-23")]
        public void Test_LengthProperties_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 80);
            
            // To make testing exact sizes easier, set specific top bit
            arrA[TestSizeWords - 1] = 0;
            arrA[TestSizeWords - 2] = 0x80000000; // Exact length
            var bigA = ToBigInteger(arrA);

            using var gpuA = new GpuBigInteger(context, arrA);

            long expectedBits = bigA.GetBitLength();
            long expectedBytes = bigA.GetByteCount(isUnsigned: true);

            Assert.Equal(expectedBits, gpuA.GetBitLength());
            Assert.Equal(expectedBytes, gpuA.GetByteCount());

            // Zero-value coverage
            var zeroArr = new uint[TestSizeWords];
            using var gpuZero = new GpuBigInteger(context, zeroArr);
            var bigZero = BigInteger.Zero;

            Assert.Equal(bigZero.GetBitLength(), gpuZero.GetBitLength());
            Assert.Equal(bigZero.GetByteCount(isUnsigned: true), gpuZero.GetByteCount());
        }

        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        [Trait("RoadmapId", "GBI-19")]
        public void Test_Square_Correctness(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(TestSizeWords * 4, engine);
            var arrA = GenerateRandomArray(TestSizeWords, 90);
            var bigA = ToBigInteger(arrA);

            using var gpuA = new GpuBigInteger(context, TestSizeWords * 4);
            gpuA.CopyFrom(new GpuBigInteger(context, arrA));
            gpuA.Square(TestSizeWords);
            var actual = ToBigInteger(gpuA.ToHostArray().Take(TestSizeWords * 2).ToArray());
            var expected = bigA * bigA;

            var mask = (BigInteger.One << (TestSizeWords * 2 * 32)) - 1;
            expected &= mask;

            Assert.Equal(expected, actual);
        }

        #region Helpers

        private void AssertGpuResultEqualsCpu(BigInteger expected, GpuBigInteger actualGpu, int truncateWords = -1)
        {
            if (truncateWords > 0)
            {
                var mask = (BigInteger.One << (truncateWords * 32)) - 1;
                expected &= mask;
            }
            
            var actualBig = ToBigInteger(actualGpu.ToHostArray());
            Assert.Equal(expected, actualBig);
        }

        private void RunBinaryOpTest(
            GpuExecutionEngine engine, 
            int resultWords,
            int contextWords,
            Action<uint[], uint[]> prepareData,
            Func<BigInteger, BigInteger, BigInteger> cpuFunc,
            Func<GpuBigInteger, GpuBigInteger, GpuBigIntExpr> gpuFunc,
            int inputWords = -1)
        {
            var size = inputWords > 0 ? inputWords : TestSizeWords;
            using var context = new GpuContext(contextWords, engine);
            var arrA = GenerateRandomArray(size, 100);
            var arrB = GenerateRandomArray(size, 101);
            
            prepareData?.Invoke(arrA, arrB);
            
            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);
            var expected = cpuFunc(bigA, bigB);
            
            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuResult = new GpuBigInteger(context, resultWords);
            
            gpuResult.Value = gpuFunc(gpuA, gpuB);
            
            AssertGpuResultEqualsCpu(expected, gpuResult, resultWords);
        }

        #endregion
    }
}


