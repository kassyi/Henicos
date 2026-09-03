using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;
using Xunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests
{
    public class GBI_01_25_GpuBigIntTests
    {
        [Fact(Skip = "未実装・課題: GBI-01 (GSharpJitProvider のメモリプール不在 / スラブアロケータ)")]
        [Trait("RoadmapId", "GBI-01")]
        [Trait("Category", "Memory")]
        public void Test_GBI01_MemoryPoolAllocation()
        {
        }

        [Fact(Skip="未実装")]
        [Trait("FeatureId", "GBI-03")]
        [Trait("Category", "Performance")]
        public void Test_GBI03_GpuReductionCompareAndFlags()
        {
        }

        private const int TestSizeWords = 256; // 8192 bits

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



        public GBI_01_25_GpuBigIntTests()
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
            GpuBigIntMath.Add(gpuA, scalarAdd, gpuResult);
            AssertGpuResultEqualsCpu(bigA + scalarAdd, gpuResult, TestSizeWords);

            // Scalar Sub
            GpuBigIntMath.Sub(gpuB, scalarSub, gpuResult);
            AssertGpuResultEqualsCpu(bigB - scalarSub, gpuResult, TestSizeWords);

            // Scalar Mul
            GpuBigIntMath.Mul(gpuA, scalarMul, gpuResult);
            AssertGpuResultEqualsCpu(bigA * scalarMul, gpuResult, TestSizeWords);
        }
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
            Buffer.BlockCopy(expectedBytes, 0, expectedWords, 0,
                System.Math.Min(expectedBytes.Length, expectedWords.Length * 4));

            var actualWords = Kassyi.GSharp.Stdlib.Math.BigInt.Algorithms.NttCpuReference.Multiply(aWords, bWords);

            for (int i = 0; i < expectedWords.Length; i++)
            {
                Assert.Equal(expectedWords[i], actualWords[i]);
            }
        }

        [Trait("RoadmapId", "GBI-11")]
        [Theory]
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
            var expected = (uint)(((ulong)a + b) % p);
            var actual = GpuBigIntNttKernels.ModAdd(a, b, p);
            Assert.Equal(expected, actual);
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

        [Fact(Skip = "未実装: GBI-16 (インクリメント/デクリメント演算子 ++, --)")]
        [Trait("RoadmapId", "GBI-16")]
        [Trait("Category", "Operator")]
        public void Test_GBI_Operator_IncrementDecrement()
        {
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
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
            AssertGpuResultEqualsCpu(bigA / bigB, gpuQ);
            AssertGpuResultEqualsCpu(bigA % bigB, gpuR);

            // Mod
            using var gpuMod = new GpuBigInteger(context, TestSizeWords);
            GpuBigIntDivRem.Mod(gpuA, gpuB, gpuMod);
            AssertGpuResultEqualsCpu(bigA % bigB, gpuMod);
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
                var actualGcd = Kassyi.GSharp.Stdlib.Math.BigInt.Algorithms.HalfGcdCpuReference.Gcd(a, b);

                Assert.Equal(expectedGcd, actualGcd);
            }
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

            GpuBigIntMath.Gcd(gpuA, gpuB, gpuResult);

            var actualGcd = ToBigInteger(gpuResult.ToHostArray());

            Assert.Equal(expectedGcd, actualGcd);
        }

        [Trait("RoadmapId", "GBI-20")]
        [Theory]
        [InlineData(GpuExecutionEngine.GSharpJit)]
        public void Test_GBI20_Gcd_EmptyCollection_ThrowsException(GpuExecutionEngine engine)
        {
            using var context = new GpuContext(10, engine);
            var arrEmpty = Array.Empty<uint>();

            Assert.ThrowsAny<Exception>(() =>
            {
                using var gpuA = new GpuBigInteger(context, arrEmpty);
                using var gpuB = new GpuBigInteger(context, arrEmpty);
                using var gpuResult = new GpuBigInteger(context, 0);

                GpuBigIntMath.Gcd(gpuA, gpuB, gpuResult);
            });
        }

        [Trait("RoadmapId", "GBI-20")]
        [Theory]
        // Happy path
        [InlineData(GpuExecutionEngine.GSharpJit, 48u, 18u)]
        // Edge cases: 0
        [InlineData(GpuExecutionEngine.GSharpJit, 0u, 0u)]
        [InlineData(GpuExecutionEngine.GSharpJit, 0u, 10u)]
        [InlineData(GpuExecutionEngine.GSharpJit, 10u, 0u)]
        // Edge cases: 1
        [InlineData(GpuExecutionEngine.GSharpJit, 1u, 1u)]
        [InlineData(GpuExecutionEngine.GSharpJit, 1u, 100u)]
        [InlineData(GpuExecutionEngine.GSharpJit, 100u, 1u)]
        // Edge cases: MaxValue and Alternating Bits
        [InlineData(GpuExecutionEngine.GSharpJit, uint.MaxValue, uint.MaxValue)]
        [InlineData(GpuExecutionEngine.GSharpJit, uint.MaxValue, 1u)]
        [InlineData(GpuExecutionEngine.GSharpJit, 0x80000000u, 0x55555555u)]
        public void Test_GBI20_Gcd_SingleElement_ReturnsCorrectGcd(GpuExecutionEngine engine, uint valA, uint valB)
        {
            using var context = new GpuContext(10, engine);
            var arrA = new uint[] { valA };
            var arrB = new uint[] { valB };

            var bigA = ToBigInteger(arrA);
            var bigB = ToBigInteger(arrB);
            var expectedGcd = BigInteger.GreatestCommonDivisor(bigA, bigB);

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);
            using var gpuResult = new GpuBigInteger(context, 1);

            GpuBigIntMath.Gcd(gpuA, gpuB, gpuResult);

            var actualGcd = ToBigInteger(gpuResult.ToHostArray());
            Assert.Equal(expectedGcd, actualGcd);
        }

        [Fact(Skip = "未実装: GBI-21 (GpuBigInteger.ModInverse モジュラ逆数)")]
        [Trait("RoadmapId", "GBI-21")]
        [Trait("Category", "Function")]
        public void Test_GBI_Function_ModInverse()
        {
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
            while (temp > 0)
            {
                bits++;
                temp >>= 1;
            }

            expectedSqrt = BigInteger.One << (bits / 2);
            for (int i = 0; i < 20; i++)
            {
                BigInteger next = (expectedSqrt + bigA / expectedSqrt) >> 1;
                if (next == expectedSqrt || next > expectedSqrt && next - expectedSqrt == 1 ||
                    expectedSqrt > next && expectedSqrt - next <= 1)
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


        [Theory(Skip =
            "Extremely slow due to cuMemAlloc overhead without memory pooling. Enable when memory pool is implemented.")]
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

            GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

            var actual = ToBigInteger(gpuResult.ToHostArray());

            Assert.Equal(expected, actual);
        }



        private void AssertGpuResultEqualsCpu(BigInteger expected, GpuBigInteger actualGpu, int truncateWords = -1)
        {
            if (truncateWords > 0)
            {
                var mask = (BigInteger.One << (truncateWords * 32)) - 1;
                expected &= mask;
            }

            var actualBig = ToBigInteger(actualGpu.ToHostArray());
            if(expected != actualBig) { System.IO.File.WriteAllText("P:\\Projects\\Active\\Henicos\\hex_diff.txt", "EXPECTED:\n" + expected.ToString("X") + "\nACTUAL:\n" + actualBig.ToString("X")); Assert.Equal(expected, actualBig); }
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

        [Theory]
        [InlineData(0u, 0u, 4253024257u, 0u)]
        [InlineData(100u, 200u, 4253024257u, 300u)]
        [InlineData(4253024256u, 1u, 4253024257u, 0u)]
        [InlineData(4253024256u, 2u, 4253024257u, 1u)]
        [InlineData(0x80000000u, 0x80000000u, 4253024257u, 41943039u)] // 2^32 % 4253024257 = 41943039
        [InlineData(4293918720u, 4293918720u, 4293918721u, 4293918719u)] // Near uint.MaxValue prime
        [InlineData(0xC0000000u, 0xC0000000u, 4293918721u, 2148532223u)] // 6442450944 - 4293918721 = 2148532223
        public void Test_ModAdd_OverflowSafe(uint a, uint b, uint prime, uint expected)
        {
            var actual = GpuBigIntNttKernels.ModAdd(a, b, prime);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Test_NttCarryResolver_ZeroLengthBuffers_ReturnsEarly()
        {
            var low = Array.Empty<uint>();
            var high = Array.Empty<uint>();
            var output = Array.Empty<uint>();

            // Assert no exceptions thrown for 0 length buffers due to guard clause
            var exception = Record.Exception(() =>
            {
                GpuBigIntNttKernelsWrapper.Launch_Kernel_ResolveNttCarries(low, high, output, 0, 0);
            });
            Assert.Null(exception);
        }

        [Fact]
        public void Test_NttCarryResolver_SingleWord_ValidatesCarryCalculation()
        {
            var low = new uint[] { 0x12345678, 0x9ABCDEF0 };
            var high = new uint[] { 0, 0 };
            var output = new uint[1];

            GpuBigIntNttKernelsWrapper.Launch_Kernel_ResolveNttCarries(low, high, output, 2, 1);

            // Calculation logic:
            // sum0 = 0x12345678 -> low16 = 0x5678, carry = 0x1234
            // sum1 = 0x1234 + 0x9ABCDEF0 = 0x9ABCF124 -> high16 = 0xF124, carry = 0x9ABC
            // output[0] = low16 | (high16 << 16) = 0x5678 | (0xF124 << 16) = 0xF1245678
            Assert.Equal(0xF1245678, output[0]);
        }

        [Fact]
        [Trait("Category", "Stress")]
        public void Test_GpuBigInt_ConcurrentAccess_DataRaceSafety()
        {
            using var scope = new JitEngine.CudaContextScope();
            var engine = GpuExecutionEngine.GSharpJit;
            using var context = new GpuContext(1024, engine);

            var arrA = GenerateRandomArray(256, 42);
            var arrB = GenerateRandomArray(256, 43);

            using var gpuA = new GpuBigInteger(context, arrA);
            using var gpuB = new GpuBigInteger(context, arrB);

            // Compute expected single-threaded baseline result
            using var expectedGpu = new GpuBigInteger(context, 256);
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
                    using var threadScope = new JitEngine.CudaContextScope();

                    using var gpuResult = new GpuBigInteger(context, 256);
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
}










