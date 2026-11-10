using static Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.GpuBigIntCoreTestHelpers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuBigIntCoreArithmeticTests
{
    public GpuBigIntCoreArithmeticTests()
    {
        CudaCapability.EnsureGpuAvailable();
        // CUDA context is initialized automatically upon first kernel launch if supported.
        // Do not initialize here to prevent xUnit discovery crashes on non-GPU CI runners.
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Test_Add_Correctness(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        // A sum is one bit wider than its operands
        RunBinaryOpTest(
            engine,
            resultWords: TestSizeWords + 1,
            contextWords: TestSizeWords * 2,
            prepareData: null,
            cpuFunc: (a, b) => a + b,
            gpuFunc: (a, b) => a + b
        );
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
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
        // A sum is one bit wider than its operands
        using var gpuResult = new GpuBigUInt(context, TestSizeWords + 1);

        gpuResult.Value = gpuA + gpuB;

        AssertGpuResultEqualsCpu(bigA + bigB, gpuResult, TestSizeWords + 1);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
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

        // Scalar Mul: the product needs one word more than the operand
        using var gpuProduct = new GpuBigUInt(context, TestSizeWords + 1);
        GpuBigIntMath.Mul(gpuA, scalarMul, gpuProduct);
        AssertGpuResultEqualsCpu(bigA * scalarMul, gpuProduct, TestSizeWords + 1);
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
    public void Test_ShiftLeft_Correctness(GpuExecutionEngine engine, int shiftBits)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 5);

        var bigA = ToBigInteger(arrA);
        var expectedBig = bigA << shiftBits;

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        // A left shift keeps every bit; truncating it to the operand width gives the value modulo 2^(32 n)
        gpuResult.Value = (gpuA << shiftBits).Truncate(TestSizeWords);

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

    // Scalar add / sub carry and borrow through every word, also beyond the 1024 threads of one block. The result
    // buffer has one word more than the operand, so a carry out of the top word is kept; a difference below zero is a
    // domain error.
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    // mutant: ScalarResultWidth.BlockSize `Math.Min(1024, ...)` -> `Math.Max` (a 1500-thread block fails to launch)
    [InlineData(0xFFFFFFFFu, true, 1u)]
    [InlineData(0u, false, 1u)]
    [InlineData(0x9E3779B9u, true, 0x7F4A7C15u)]
    [InlineData(0x9E3779B9u, false, 0x7F4A7C15u)]
    public void ScalarAddSub_MoreThan1024Words_MatchesBigInteger(uint fill, bool add, uint scalar)
    {
        CudaCapability.EnsureGpuAvailable();
        const int N = 1500;
        using var ctx = new GpuWorkspace(4 * N, GpuExecutionEngine.GSharpAot);

        var words = new uint[N];
        Array.Fill(words, fill);
        var value = WordsToBigInteger(words);
        using var operand = new GpuBigUInt(ctx, words);
        using var result = new GpuBigUInt(ctx, N + 1);

        if (add)
        {
            GpuBigIntMath.Add(operand, scalar, result);
        }
        else
        {
            GpuBigIntMath.Sub(operand, scalar, result);
        }

        var expected = add ? value + scalar : value - scalar;
        if (expected.Sign < 0)
        {
            Assert.Throws<OverflowException>(() => result.ToBigInteger());
            return;
        }

        Assert.Equal(expected, result.ToBigInteger());
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void DivideByUInt32_ZeroDivisor_Throws()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        using var dividend = new GpuBigUInt(ctx, [12345u, 1u, 0u, 0u]);
        using var quotient = new GpuBigUInt(ctx, 4);

        // mutant: KernelBasicMathHandler.cs:84-87 throw removed (the kernel divides by 0 without an error)
        Assert.Throws<DivideByZeroException>(() => GpuBigIntMath.DivideByUInt32(dividend, 0u, quotient));
    }
}
