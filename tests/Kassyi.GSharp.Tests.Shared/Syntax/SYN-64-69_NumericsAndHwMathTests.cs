using System.Numerics;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_64_69_NumericsAndHwMathTests
:  TranspilerTestBase{
    [Fact()]
    [Trait("FeatureId", "SYN-64")]
    [Trait("Category", "Syntax")]
    public void Test_SYN64_HalfPrecision_Transpilation()
    {
    }

    [Fact()]
    [Trait("FeatureId", "SYN-65")]
    [Trait("Category", "Syntax")]
    public void Test_SYN65_NativeInt_Transpilation()
    {
    }

    [Fact()]
    [Trait("FeatureId", "SYN-66")]
    [Trait("Category", "Syntax")]
    public void Test_SYN66_Int128UInt128_Transpilation()
    {
    }

    [Fact()]
    [Trait("FeatureId", "SYN-67")]
    [Trait("Category", "Syntax")]
    public void Test_SYN67_BFloat16Fp8_Transpilation()
    {
    }
    [SkippableTheory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-68")]
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

        SYN_64_69_NumericsAndHwMathKernelsWrapper.Launch_BitOperationsTestKernel(u32Data, u64Data);

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
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-68")]
    [InlineData(0.0, 0.0f)]
    [InlineData(1.5707963267948966, 1.5707963f)] // pi / 2
    [InlineData(-3.141592653589793, -3.1415927f)] // -pi
    [InlineData(12.34, 12.34f)]
    [InlineData(-5.67, -5.67f)]
    [InlineData(1.0, 1.0f)]
    [InlineData(-1.0, -1.0f)]
    public void Test_SYN68_MathAndMathF_DataDriven(double dVal, float fVal)
    {
        double[] doubleInputs = [dVal];
        double[] doubleOutputs = new double[9];
        float[] floatInputs = [fVal];
        float[] floatOutputs = new float[9];

        SYN_49_SYN_68_InvocationAndIntrinsicsKernelsWrapper.Launch_MathKernel(
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
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-68")]
    [InlineData(float.NaN, double.NaN)]
    [InlineData(float.PositiveInfinity, double.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, double.NegativeInfinity)]
    [InlineData(float.MaxValue, double.MaxValue)]
    [InlineData(float.MinValue, double.MinValue)]
    [InlineData(float.Epsilon, double.Epsilon)]
    [InlineData(-0.0f, -0.0)]
    public void Test_SYN68_MathEdgeCases_SpecialValues(float fInput, double dInput)
    {
        float[] floatInputs = [fInput];
        float[] floatResults = new float[6];
        double[] doubleInputs = [dInput];
        double[] doubleResults = new double[6];

        SYN_49_SYN_68_InvocationAndIntrinsicsKernelsWrapper.Launch_MathEdgeCasesKernel(
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
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "SYN-68")]
    public void Test_SYN68_MathAndMathF_EmptyArray()
    {
        double[] doubleInputs = [];
        double[] doubleOutputs = [];
        float[] floatInputs = [];
        float[] floatOutputs = [];

        var ex = Record.Exception(() => 
            SYN_49_SYN_68_InvocationAndIntrinsicsKernelsWrapper.Launch_MathKernel(
                doubleInputs, doubleOutputs, floatInputs, floatOutputs, blockDimX: 0));

        Assert.Null(ex);
    }

    [Fact()]
    [Trait("FeatureId", "SYN-69")]
    [Trait("Category", "Syntax")]
    public void Test_SYN69_AddWithCarry64_Transpilation()
    {
    }
}
