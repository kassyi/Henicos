using System;
using System.Threading;
using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class SYN_49_SYN_68_InvocationAndIntrinsicsTests
{
    // SYN-49 Happy Path
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-49")]
    public void Test_SYN49_InterlockedAtomicOperations()
    {
        int[] intBuf = new int[3];
        uint[] uintBuf = new uint[3];
        long[] longBuf = new long[3];
        ulong[] ulongBuf = new ulong[3];

        const int numThreads = 64;
        SYN_49_SYN_68_InvocationAndIntrinsicsKernelsWrapper.Launch_InterlockedKernel(
            intBuf, uintBuf, longBuf, ulongBuf, blockDimX: numThreads);

        Assert.Equal(numThreads, intBuf[0]);
        Assert.Equal(numThreads * 2L, longBuf[0]);

        Assert.Equal(100, intBuf[1]);
        Assert.Equal(200u, uintBuf[1]);
        Assert.Equal(300L, longBuf[1]);
        Assert.Equal(400ul, ulongBuf[1]);

        Assert.Equal(500, intBuf[2]);
        Assert.Equal(600u, uintBuf[2]);
        Assert.Equal(700L, longBuf[2]);
        Assert.Equal(800ul, ulongBuf[2]);
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-49")]
    public void Test_SYN49_InterlockedAtomicOperations_EmptyArray()
    {
        int[] intBuf = [];
        uint[] uintBuf = [];
        long[] longBuf = [];
        ulong[] ulongBuf = [];

        var ex = Record.Exception(() => 
            SYN_49_SYN_68_InvocationAndIntrinsicsKernelsWrapper.Launch_InterlockedKernel(
                intBuf, uintBuf, longBuf, ulongBuf, blockDimX: 0));

        Assert.Null(ex);
    }

    // SYN-49 Edge Cases
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-49")]
    public void Test_SYN49_InterlockedEdgeCases_ArrayElementsAndStructFields()
    {
        int[] array = new int[4] { 1, 2, 3, 4 };
        AtomicTestStruct[] structArray = new AtomicTestStruct[4]
        {
            new AtomicTestStruct { ValueI = 10, ValueU = 0, ValueL = 0 },
            new AtomicTestStruct { ValueI = 20, ValueU = 0, ValueL = 0 },
            new AtomicTestStruct { ValueI = 30, ValueU = 0, ValueL = 0 },
            new AtomicTestStruct { ValueI = 40, ValueU = 0, ValueL = 0 },
        };

        SYN_49_SYN_68_InvocationAndIntrinsicsKernelsWrapper.Launch_InterlockedEdgeCasesKernel(
            array, structArray, blockDimX: 4);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i + 1 + 10, array[i]);
            Assert.Equal((i + 1) * 10 + 1, structArray[i].ValueI);
            Assert.Equal(42u, structArray[i].ValueU);
            Assert.Equal(99L, structArray[i].ValueL);
        }
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "SYN-49")]
    public void Test_SYN49_InterlockedEdgeCases_EmptyArray()
    {
        int[] emptyArray = [];
        AtomicTestStruct[] emptyStructArray = [];

        // Should be rejected or safely handled (we assert no exception is thrown when blockDimX is 0, or we catch exception)
        var ex = Record.Exception(() => 
            SYN_49_SYN_68_InvocationAndIntrinsicsKernelsWrapper.Launch_InterlockedEdgeCasesKernel(
                emptyArray, emptyStructArray, blockDimX: 0));

        Assert.Null(ex);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "SYN-49")]
    public void Test_AtomicTestStruct_MemoryLayout()
    {
        Assert.Equal(16, System.Runtime.InteropServices.Marshal.SizeOf<AtomicTestStruct>());
        Assert.Equal(0, System.Runtime.InteropServices.Marshal.OffsetOf<AtomicTestStruct>("ValueI").ToInt32());
        Assert.Equal(4, System.Runtime.InteropServices.Marshal.OffsetOf<AtomicTestStruct>("ValueU").ToInt32());
        Assert.Equal(8, System.Runtime.InteropServices.Marshal.OffsetOf<AtomicTestStruct>("ValueL").ToInt32());
    }

    // SYN-68 Happy Path
    [Theory]
    [Trait("Category", "Execution")]
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
        double[] doubleOutputs = new double[6];
        float[] floatInputs = [fVal];
        float[] floatOutputs = new float[6];

        SYN_49_SYN_68_InvocationAndIntrinsicsKernelsWrapper.Launch_MathKernel(
            doubleInputs, doubleOutputs, floatInputs, floatOutputs, blockDimX: 1);

        // Double assertions
        Assert.Equal(Math.Sin(dVal), doubleOutputs[0], precision: 5);
        Assert.Equal(Math.Cos(dVal), doubleOutputs[1], precision: 5);
        Assert.Equal(Math.Clamp(dVal, 0.0, 10.0), doubleOutputs[2]);
        Assert.Equal(Math.Max(dVal, 5.0), doubleOutputs[3]);
        Assert.Equal(Math.Min(dVal, 5.0), doubleOutputs[4]);
        Assert.Equal(Math.Abs(dVal), doubleOutputs[5]);

        // Float assertions
        Assert.Equal(MathF.Sin(fVal), floatOutputs[0], precision: 4);
        Assert.Equal(MathF.Cos(fVal), floatOutputs[1], precision: 4);
        Assert.Equal(Math.Clamp(fVal, 0.0f, 10.0f), floatOutputs[2]);
        Assert.Equal(MathF.Max(fVal, 5.0f), floatOutputs[3]);
        Assert.Equal(MathF.Min(fVal, 5.0f), floatOutputs[4]);
        Assert.Equal(MathF.Abs(fVal), floatOutputs[5]);
    }

    // SYN-68 Edge Cases: NaN / Infinity / Extremes
    [Theory]
    [Trait("Category", "Execution")]
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
        float[] floatResults = new float[3];
        double[] doubleInputs = [dInput];
        double[] doubleResults = new double[3];

        SYN_49_SYN_68_InvocationAndIntrinsicsKernelsWrapper.Launch_MathEdgeCasesKernel(
            floatInputs, floatResults, doubleInputs, doubleResults, blockDimX: 1);

        if (float.IsNaN(fInput))
        {
            Assert.True(float.IsNaN(floatResults[0])); // Abs(NaN) is NaN
            Assert.Equal(0.0f, floatResults[1]); // fmaxf(NaN, 0) is 0
            Assert.Equal(0.0f, floatResults[2]); // fminf(NaN, 0) is 0
        }
        else
        {
            Assert.Equal(MathF.Abs(fInput), floatResults[0]);
            Assert.Equal(MathF.Max(fInput, 0.0f), floatResults[1]);
            Assert.Equal(MathF.Min(fInput, 0.0f), floatResults[2]);
        }

        if (double.IsNaN(dInput))
        {
            Assert.True(double.IsNaN(doubleResults[0])); // Abs(NaN) is NaN
            Assert.Equal(0.0, doubleResults[1]); // fmax(NaN, 0) is 0
            Assert.Equal(0.0, doubleResults[2]); // fmin(NaN, 0) is 0
        }
        else
        {
            Assert.Equal(Math.Abs(dInput), doubleResults[0]);
            Assert.Equal(Math.Max(dInput, 0.0), doubleResults[1]);
            Assert.Equal(Math.Min(dInput, 0.0), doubleResults[2]);
        }
    }

    [Fact]
    [Trait("Category", "Execution")]
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
}
