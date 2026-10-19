using System.Numerics;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class NumericsAndHwMathTests : TranspilerTestBase
{
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [InlineData(0u, 0ul)]
    [InlineData(1u, 1ul)]
    [InlineData(uint.MaxValue, ulong.MaxValue)]
    [InlineData(0x80000000u, 0x8000000000000000ul)]
    [InlineData(0x55555555u, 0x5555555555555555ul)]
    [InlineData(0xAAAAAAAAu, 0xAAAAAAAAAAAAAAAAul)]
    [InlineData(0b00000000_10101010_11110000_00001000u, 0b00000000_00000000_10101010_11110000_00001000_00000000_00000000_00001000ul)]
    public void TestBitOperationsKernel_DataDriven(uint u32Input, ulong u64Input)
    {
        uint[] u32Data = new uint[7];
        u32Data[0] = u32Input;

        ulong[] u64Data = new ulong[7];
        u64Data[0] = u64Input;

        NumericsAndHwMathKernels.LaunchBitOperationsTestKernel(u32Data, u64Data);

        uint u32Val = u32Data[0];
        Assert.Equal((uint)BitOperations.LeadingZeroCount(u32Val), u32Data[1]);
        Assert.Equal((uint)BitOperations.PopCount(u32Val), u32Data[2]);
        Assert.Equal((uint)BitOperations.TrailingZeroCount(u32Val), u32Data[3]);
        Assert.Equal((uint)BitOperations.Log2(u32Val), u32Data[4]);
        Assert.Equal(BitOperations.RotateLeft(u32Val, 5), u32Data[5]);
        Assert.Equal(BitOperations.RotateRight(u32Val, 3), u32Data[6]);

        ulong u64Val = u64Data[0];
        Assert.Equal((ulong)BitOperations.LeadingZeroCount(u64Val), u64Data[1]);
        Assert.Equal((ulong)BitOperations.PopCount(u64Val), u64Data[2]);
        Assert.Equal((ulong)BitOperations.TrailingZeroCount(u64Val), u64Data[3]);
        Assert.Equal((ulong)BitOperations.Log2(u64Val), u64Data[4]);
        Assert.Equal(BitOperations.RotateLeft(u64Val, 10), u64Data[5]);
        Assert.Equal(BitOperations.RotateRight(u64Val, 7), u64Data[6]);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [InlineData(0.0, 0.0f)]
    [InlineData(1.5707963267948966, 1.5707963f)] // pi / 2
    [InlineData(-3.141592653589793, -3.1415927f)] // -pi
    [InlineData(12.34, 12.34f)]
    [InlineData(-5.67, -5.67f)]
    [InlineData(1.0, 1.0f)]
    [InlineData(-1.0, -1.0f)]
    public void Test_MathAndMathF_DataDriven(double dVal, float fVal)
    {
        double[] doubleInputs = [dVal];
        double[] doubleOutputs = new double[9];
        float[] floatInputs = [fVal];
        float[] floatOutputs = new float[9];

        InvocationAndIntrinsicsKernels.LaunchMathKernel(
            doubleInputs, doubleOutputs, floatInputs, floatOutputs, blockDimX: 1);

        // Double assertions
        Assert.Equal(Math.Sin(dVal), doubleOutputs[0], precision: 5);
        Assert.Equal(Math.Cos(dVal), doubleOutputs[1], precision: 5);
        Assert.Equal(Math.Clamp(dVal, 0.0, 10.0), doubleOutputs[2]);
        Assert.Equal(Math.Max(dVal, 5.0), doubleOutputs[3]);
        Assert.Equal(Math.Min(dVal, 5.0), doubleOutputs[4]);
        Assert.Equal(Math.Abs(dVal), doubleOutputs[5]);
        Assert.Equal(Math.Exp(dVal), doubleOutputs[6], precision: 5);
        Assert.Equal(Math.Sqrt(Math.Abs(dVal)), doubleOutputs[7], precision: 5);
        Assert.Equal(Math.Log(Math.Abs(dVal) + 1.0), doubleOutputs[8], precision: 5);

        // Float assertions
        Assert.Equal(MathF.Sin(fVal), floatOutputs[0], precision: 4);
        Assert.Equal(MathF.Cos(fVal), floatOutputs[1], precision: 4);
        Assert.Equal(Math.Clamp(fVal, 0.0f, 10.0f), floatOutputs[2]);
        Assert.Equal(MathF.Max(fVal, 5.0f), floatOutputs[3]);
        Assert.Equal(MathF.Min(fVal, 5.0f), floatOutputs[4]);
        Assert.Equal(MathF.Abs(fVal), floatOutputs[5]);
        Assert.Equal(MathF.Exp(fVal), floatOutputs[6], precision: 1);
        Assert.Equal(MathF.Sqrt(MathF.Abs(fVal)), floatOutputs[7], precision: 4);
        Assert.Equal(MathF.Log(MathF.Abs(fVal) + 1.0f), floatOutputs[8], precision: 4);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [InlineData(float.NaN, double.NaN)]
    [InlineData(float.PositiveInfinity, double.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, double.NegativeInfinity)]
    [InlineData(float.MaxValue, double.MaxValue)]
    [InlineData(float.MinValue, double.MinValue)]
    [InlineData(float.Epsilon, double.Epsilon)]
    [InlineData(-0.0f, -0.0)]
    public void Test_MathEdgeCases_SpecialValues(float fInput, double dInput)
    {
        if (float.IsNaN(fInput) || double.IsNaN(dInput))
        {
            SkipIfCpuDialect("CUDA and C# differ in NaN handling for Math.Max and Math.Min");
        }

        float[] floatInputs = [fInput];
        float[] floatResults = new float[6];
        double[] doubleInputs = [dInput];
        double[] doubleResults = new double[6];

        InvocationAndIntrinsicsKernels.LaunchMathEdgeCasesKernel(
            floatInputs, floatResults, doubleInputs, doubleResults, blockDimX: 1);

        if (float.IsNaN(fInput))
        {
            Assert.True(float.IsNaN(floatResults[0])); // Abs(NaN) is NaN
            Assert.Equal(0.0f, floatResults[1]); // fmaxf(NaN, 0) is 0
            Assert.Equal(0.0f, floatResults[2]); // fminf(NaN, 0) is 0
            Assert.True(float.IsNaN(floatResults[3]));
            Assert.True(float.IsNaN(floatResults[4]));
            Assert.True(float.IsNaN(floatResults[5]));
        }
        else
        {
            Assert.Equal(MathF.Abs(fInput), floatResults[0]);
            Assert.Equal(MathF.Max(fInput, 0.0f), floatResults[1]);
            Assert.Equal(MathF.Min(fInput, 0.0f), floatResults[2]);
            Assert.Equal(MathF.Exp(fInput), floatResults[3]);
            Assert.Equal(MathF.Sqrt(fInput), floatResults[4]);
            Assert.Equal(MathF.Log(fInput), floatResults[5]);
        }

        if (double.IsNaN(dInput))
        {
            Assert.True(double.IsNaN(doubleResults[0])); // Abs(NaN) is NaN
            Assert.Equal(0.0, doubleResults[1]); // fmax(NaN, 0) is 0
            Assert.Equal(0.0, doubleResults[2]); // fmin(NaN, 0) is 0
            Assert.True(double.IsNaN(doubleResults[3]));
            Assert.True(double.IsNaN(doubleResults[4]));
            Assert.True(double.IsNaN(doubleResults[5]));
        }
        else
        {
            Assert.Equal(Math.Abs(dInput), doubleResults[0]);
            Assert.Equal(Math.Max(dInput, 0.0), doubleResults[1]);
            Assert.Equal(Math.Min(dInput, 0.0), doubleResults[2]);
            Assert.Equal(Math.Exp(dInput), doubleResults[3]);
            Assert.Equal(Math.Sqrt(dInput), doubleResults[4]);
            Assert.Equal(Math.Log(dInput), doubleResults[5]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_MathAndMathF_EmptyArray()
    {
        double[] doubleInputs = [];
        double[] doubleOutputs = [];
        float[] floatInputs = [];
        float[] floatOutputs = [];

        // Empty array arguments are rejected instead of silently skipping the launch
        var ex = Assert.Throws<ArgumentException>(() =>
            InvocationAndIntrinsicsKernels.LaunchMathKernel(
                doubleInputs, doubleOutputs, floatInputs, floatOutputs, blockDimX: 0));

        Assert.Equal("doubleInputs", ex.ParamName);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    public void Test_PtxIntrinsicsExpansion_Execution()
    {
        SkipIfCpuDialect("PTX hardware intrinsics require CUDA execution environment");

        uint[] u32Out = new uint[10];
        ulong[] u64Out = new ulong[10];
        int[] i32Out = new int[5];

        NumericsAndHwMathKernels.LaunchPtxIntrinsicsExpansionKernel(u32Out, u64Out, i32Out, blockDimX: 1);

        // 0x12345678, pos=4, len=8 -> bits 4..11: (0x12345678 >> 4) & 0xFF = 0x67
        Assert.Equal(0x67u, u32Out[0]);
        // 0x0123456789ABCDEF, pos=8, len=16 -> (val >> 8) & 0xFFFF = 0xABCD
        Assert.Equal(0xABCDul, u64Out[0]);

        // BFI 32-bit: insert 0xFF into 0x12345678 at pos=4, len=8 -> 0x12345FF8
        Assert.Equal(0x12345FF8u, u32Out[1]);
        // BFI 64-bit: insert 0xFFFF into 0x0123456789ABCDEF at pos=8, len=16 -> 0x0123456789FFFFEF
        Assert.Equal(0x0123456789FFFFEFul, u64Out[1]);

        // Lop3: F(A, B, C) with LUT 0x54: (A | B) & ~C
        // A=0x12345678, B=0x0F0F0F0F, C=0xAAAAAAAA
        uint expectedLop3 = (0x12345678u | 0x0F0F0F0Fu) & ~0xAAAAAAAAu;
        Assert.Equal(expectedLop3, u32Out[2]);

        // CountTrailingZeros: 0x12345678 has 3 trailing zeros; 0x0123456789ABCDEF has 0 trailing zeros
        Assert.Equal(3u, u32Out[3]);
        Assert.Equal(0ul, u64Out[2]);

        // BitReverse64(0x0123456789ABCDEFul) == 0xF7B3D591E6A2C480ul
        Assert.Equal(0xF7B3D591E6A2C480ul, u64Out[3]);

        // DotProduct4: (1*5 + 2*6 + 3*7 + 4*8) + 10 = (5 + 12 + 21 + 32) + 10 = 70 + 10 = 80
        Assert.Equal(80, i32Out[0]);
        Assert.Equal(80u, u32Out[4]);

        // Sad4: |0x10-0x01| + |0x20-0x02| + |0x30-0x03| + |0x40-0x04| + 5 = 15 + 30 + 45 + 60 + 5 = 155
        Assert.Equal(155u, u32Out[5]);

        // MatchAnySync & MatchAllSync: lane 0 matches itself (bit 0 set)
        Assert.True((u32Out[6] & 1u) != 0u);
        Assert.True((u32Out[7] & 1u) != 0u);

        // ReductionAdd: 42u
        Assert.Equal(42u, u32Out[8]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_HalfPrecision_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<NumericsAndHwMathTarget>(
            nameof(NumericsAndHwMathTarget.HalfPrecision), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        Half[] values = [(Half)1.5f];
        NumericsAndHwMathTarget.HalfPrecision(values);
        Assert.Equal((Half)1.5f, values[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_NativeInt_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<NumericsAndHwMathTarget>(
            nameof(NumericsAndHwMathTarget.NativeInt), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        nint[] signedOffsets = [10];
        nuint[] unsignedOffsets = [20u];
        NumericsAndHwMathTarget.NativeInt(signedOffsets, unsignedOffsets);
        Assert.Equal((nint)11, signedOffsets[0]);
        Assert.Equal((nuint)21u, unsignedOffsets[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_Int128UInt128_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<NumericsAndHwMathTarget>(
            nameof(NumericsAndHwMathTarget.Int128UInt128), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        Int128[] signed128 = [100];
        UInt128[] unsigned128 = [200];
        NumericsAndHwMathTarget.Int128UInt128(signed128, unsigned128);
        Assert.Equal((Int128)100, signed128[0]);
        Assert.Equal((UInt128)200, unsigned128[0]);
    }

    [Fact]
    [Category(TestCategory.Syntax)]
    public void Test_BFloat16Fp8_Transpilation()
    {
        var (cudaSource, diagnostics) = RoslynTestUtility.TranspileMethod<NumericsAndHwMathTarget>(
            nameof(NumericsAndHwMathTarget.BFloat16Fp8), dialect: CurrentDialect);

        Assert.Empty(diagnostics);
        Assert.False(string.IsNullOrEmpty(cudaSource));

        BFloat16[] bf16 = [new() { Bits = 0x1234 }];
        Float8E4M3[] fp8 = [new() { Bits = 0x56 }];
        NumericsAndHwMathTarget.BFloat16Fp8(bf16, fp8);
        Assert.Equal((ushort)0x1234, bf16[0].Bits);
        Assert.Equal((byte)0x56, fp8[0].Bits);
    }
}

public struct BFloat16
{
    public ushort Bits;
}

public struct Float8E4M3
{
    public byte Bits;
}

public class NumericsAndHwMathTarget
{
    public static void HalfPrecision(Half[] values)
    {
        Half h = values[0];
        values[0] = h;
    }

    public static void NativeInt(nint[] signedOffsets, nuint[] unsignedOffsets)
    {
        nint s = signedOffsets[0];
        nuint u = unsignedOffsets[0];
        signedOffsets[0] = s + 1;
        unsignedOffsets[0] = u + 1u;
    }

    public static void Int128UInt128(Int128[] signed128, UInt128[] unsigned128)
    {
        Int128 s = signed128[0];
        UInt128 u = unsigned128[0];
        signed128[0] = s;
        unsigned128[0] = u;
    }

    public static void BFloat16Fp8(BFloat16[] bf16, Float8E4M3[] fp8)
    {
        BFloat16 b = bf16[0];
        Float8E4M3 f = fp8[0];
        bf16[0] = b;
        fp8[0] = f;
    }
}

