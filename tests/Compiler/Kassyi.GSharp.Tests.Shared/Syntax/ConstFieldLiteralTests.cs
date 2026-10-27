namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A reference to a <c>const</c> field (and an omitted argument) is written as a literal. Each test reads the values
/// back and compares them with the same fields evaluated by C#. Floating-point values are compared bit for bit, so that
/// a negative zero or a rounded <see cref="float.Epsilon"/> does not pass as equal; a NaN only has to stay a NaN,
/// because C# and C++ do not agree on its bits.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class ConstFieldLiteralTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_CharConst_KeepsCharacterCode_Execution()
    {
        int[] output = new int[8];
        char[] text = new char[2];

        ConstFieldLiteralKernels.LaunchCharConstKernel(output, text, blockDimX: 1);

        Assert.Equal(
            [
                ConstFieldLiteralKernels.Zero,
                ConstFieldLiteralKernels.LetterA,
                ConstFieldLiteralKernels.NewLine,
                ConstFieldLiteralKernels.EAcute,
                ConstFieldLiteralKernels.Nul,
                ConstFieldLiteralKernels.Backslash,
                ConstFieldLiteralKernels.Quote,
                (int)ConstFieldLiteralKernels.LastBmp
            ],
            output);
        Assert.Equal([ConstFieldLiteralKernels.LetterA, ConstFieldLiteralKernels.LastBmp], text);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntegerLimitConst_KeepsValue_Execution()
    {
        long[] signed = new long[4];
        ulong[] unsigned = new ulong[2];
        int[] narrow = new int[2];

        ConstFieldLiteralKernels.LaunchIntegerLimitConstKernel(signed, unsigned, narrow, blockDimX: 1);

        Assert.Equal([int.MinValue, long.MinValue, int.MaxValue, long.MaxValue], signed);
        Assert.Equal([uint.MaxValue, ulong.MaxValue], unsigned);
        Assert.Equal([int.MinValue, int.MinValue / 2], narrow);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_EnumConst_KeepsValue_Execution()
    {
        ulong[] wide = new ulong[2];
        int[] signed = new int[3];

        ConstFieldLiteralKernels.LaunchEnumConstKernel(wide, signed, blockDimX: 1);

        Assert.Equal([(ulong)ConstFieldLiteralWideEnum.Small, (ulong)ConstFieldLiteralWideEnum.Large], wide);
        Assert.Equal(
            [
                (int)ConstFieldLiteralSignedEnum.Lowest,
                (int)ConstFieldLiteralSignedEnum.Negative,
                (int)ConstFieldLiteralSignedEnum.Highest
            ],
            signed);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SingleConst_KeepsValue_Execution()
    {
        float[] output = new float[9];

        ConstFieldLiteralKernels.LaunchSingleConstKernel(output, blockDimX: 1);

        AssertSameBits(
            [
                ConstFieldLiteralKernels.WholeSingle,
                ConstFieldLiteralKernels.HalfSingle,
                float.Epsilon,
                ConstFieldLiteralKernels.NegativeZeroSingle,
                float.MaxValue
            ],
            output[..5]);
        Assert.True(float.IsNaN(output[5]), $"float.NaN became {output[5]}");
        Assert.Equal(float.PositiveInfinity, output[6]);
        Assert.Equal(float.NegativeInfinity, output[7]);
        AssertSameBits([3.0f * ConstFieldLiteralKernels.HalfSingle], output[8..]);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_DoubleConst_KeepsValue_Execution()
    {
        double[] output = new double[10];

        ConstFieldLiteralKernels.LaunchDoubleConstKernel(output, blockDimX: 1);

        AssertSameBits(
            [
                ConstFieldLiteralKernels.HalfDouble,
                ConstFieldLiteralKernels.WholeDouble,
                ConstFieldLiteralKernels.HugeDouble,
                double.Epsilon,
                ConstFieldLiteralKernels.NegativeZeroDouble,
                double.MaxValue
            ],
            output[..6]);
        Assert.True(double.IsNaN(output[6]), $"double.NaN became {output[6]}");
        Assert.Equal(double.PositiveInfinity, output[7]);
        Assert.Equal(double.NegativeInfinity, output[8]);
        AssertSameBits([3.0 * ConstFieldLiteralKernels.HalfDouble], output[9..]);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_OmittedArgument_UsesDefaultValue_Execution()
    {
        float[] singles = new float[3];
        double[] doubles = new double[2];
        long[] longs = new long[2];
        int[] codes = new int[2];

        ConstFieldLiteralKernels.LaunchOmittedArgumentKernel(singles, doubles, longs, codes, blockDimX: 1);

        AssertSameBits(
            [ConstFieldLiteralKernels.ScaleBy(3f), ConstFieldLiteralKernels.ScaleByAttribute(3f)],
            singles[..2]);
        Assert.True(float.IsNaN(singles[2]), $"[DefaultParameterValue(float.NaN)] became {singles[2]}");
        AssertSameBits(
            [ConstFieldLiteralKernels.OffsetBy(0.5), ConstFieldLiteralKernels.OffsetByAttribute(0.5)],
            doubles);
        Assert.Equal([ConstFieldLiteralKernels.Widen(), ConstFieldLiteralKernels.WidenByAttribute()], longs);
        Assert.Equal([ConstFieldLiteralKernels.CodeOf(), ConstFieldLiteralKernels.CodeOfAttribute()], codes);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_KernelDefault_LauncherUsesDefaultValue_Execution()
    {
        float[] singles = new float[2];
        double[] doubles = new double[1];
        long[] longs = new long[2];
        int[] codes = new int[1];

        ConstFieldLiteralKernels.LaunchKernelDefaultKernel(singles, doubles, longs, codes, blockDimX: 1);

        AssertSameBits([1.5f], singles[..1]);
        Assert.True(float.IsNaN(singles[1]), $"float.NaN became {singles[1]}");
        AssertSameBits([0.5], doubles);
        Assert.Equal([long.MinValue, int.MinValue], longs);
        Assert.Equal([(int)ConstFieldLiteralKernels.LetterA], codes);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_EnumKernelDefault_LauncherCastsBaseValue_Execution()
    {
        ulong[] wide = new ulong[1];
        int[] signed = new int[1];

        ConstFieldLiteralKernels.LaunchEnumDefaultKernel(wide, signed, blockDimX: 1);

        Assert.Equal([(ulong)ConstFieldLiteralWideEnum.Large], wide);
        Assert.Equal([(int)ConstFieldLiteralSignedEnum.Lowest], signed);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NegativeOperand_DoesNotMergeWithOperator_Execution()
    {
        int x = 10;
        int[] ints = new int[6];
        long[] longs = new long[1];
        float[] singles = new float[2];

        ConstFieldLiteralKernels.LaunchNegativeOperandKernel(ints, longs, singles, [x], blockDimX: 1);

        Assert.Equal(
            [
                x - ConstFieldLiteralKernels.Negative,
                x - -5,
                - -x,
                x + +x,
                x - -x * 2,
                x - - -x
            ],
            ints);
        Assert.Equal([x - ConstFieldLiteralKernels.NegativeLong], longs);
        AssertSameBits(
            [-ConstFieldLiteralKernels.NegativeZeroSingle, 1.0f - -ConstFieldLiteralKernels.NegativeZeroSingle],
            singles);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NegativeLiteral_KeepsValueWhenWidened_Execution()
    {
        long[] longs = new long[3];
        double[] doubles = new double[3];

        ConstFieldLiteralKernels.LaunchNegativeLiteralKernel(longs, doubles, blockDimX: 1);

        Assert.Equal([-2147483648, -9223372036854775808, -5], longs);
        AssertSameBits([(double)-2147483648, -0.0, -1.5], doubles);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntegerSuffix_KeepsCSharpType_Execution()
    {
        uint factor = 2;
        const int shift = 40;
        ulong[] output = new ulong[4];
        var seven = ConstFieldLiteralKernels.SevenU;

        ConstFieldLiteralKernels.LaunchSuffixKernel(output, factor, shift, blockDimX: 1);

        Assert.Equal(
            [
                uint.MaxValue * factor,
                (ulong)(ConstFieldLiteralKernels.OneLong << shift),
                ConstFieldLiteralKernels.OneULong << (shift + 23),
                seven - 8
            ],
            output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableDefault_UsesDefaultValue_Execution()
    {
        float[] singles = new float[1];
        int[] codes = new int[2];

        ConstFieldLiteralKernels.LaunchNullableDefaultKernel(singles, codes, blockDimX: 1);

        Assert.True(float.IsNaN(singles[0]), $"[DefaultParameterValue(float.NaN)] float? became {singles[0]}");
        Assert.Equal(
            [ConstFieldLiteralKernels.NullableCodeByAttribute(), ConstFieldLiteralKernels.NullableCode()],
            codes);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NegativeUnrollStart_Execution()
    {
        int x = 10;
        int[] output = new int[4];

        ConstFieldLiteralKernels.LaunchNegativeUnrollKernel(output, [x], blockDimX: 1);

        // i = -2, -1, 0 (skipped), 1
        Assert.Equal([x - -2, x - -1, 0, x - 1], output);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SynthesizedDefault_KeepsValue_Execution()
    {
        long[] longs = new long[7];
        float[] singles = new float[1];
        double[] doubles = new double[5];

        ConstFieldLiteralKernels.LaunchSynthesizedDefaultKernel(longs, singles, doubles, blockDimX: 1);

        Assert.Equal([1, int.MinValue, long.MinValue, int.MinValue, long.MinValue, int.MinValue, long.MinValue], longs);
        Assert.True(float.IsNaN(singles[0]), $"the omitted float.NaN became {singles[0]}");
        Assert.True(double.IsNaN(doubles[0]), $"the omitted float.NaN of the iterator became {doubles[0]}");
        Assert.Equal(double.PositiveInfinity, doubles[1]);
        Assert.True(double.IsNaN(doubles[2]), $"the omitted double.NaN of the iterator became {doubles[2]}");
        Assert.Equal(double.NegativeInfinity, doubles[3]);
        // [Optional] without [DefaultParameterValue]: default(double)
        Assert.Equal(0.0, doubles[4]);
    }

    private static void AssertSameBits(float[] expected, float[] actual) =>
        Assert.Equal(
            Array.ConvertAll(expected, BitConverter.SingleToInt32Bits),
            Array.ConvertAll(actual, BitConverter.SingleToInt32Bits));

    private static void AssertSameBits(double[] expected, double[] actual) =>
        Assert.Equal(
            Array.ConvertAll(expected, BitConverter.DoubleToInt64Bits),
            Array.ConvertAll(actual, BitConverter.DoubleToInt64Bits));
}
