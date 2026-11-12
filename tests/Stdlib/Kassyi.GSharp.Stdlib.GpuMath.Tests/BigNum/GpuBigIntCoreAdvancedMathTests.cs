using static Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.GpuBigIntCoreTestHelpers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuBigIntCoreAdvancedMathTests
{
    public GpuBigIntCoreAdvancedMathTests()
    {
        CudaCapability.EnsureGpuAvailable();
        // CUDA context is initialized automatically upon first kernel launch if supported.
        // Do not initialize here to prevent xUnit discovery crashes on non-GPU CI runners.
    }

    [SkippableFact]
    [Category(TestCategory.Compiler)]
    public void Square_DirectPointwiseKernel_MatchesBigIntegerReference()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        using var a = new GpuBigUInt(ctx, new uint[] { 123456789, 0, 0, 0 });
        using var result = new GpuBigUInt(ctx, 8);

        // Should invoke direct square kernel instead of generic multiply
        result.Value = a * a;

        var expected = ToBigInteger(new uint[] { 123456789, 0, 0, 0 }) *
                       ToBigInteger(new uint[] { 123456789, 0, 0, 0 });
        Assert.Equal(expected, ToBigInteger(result.ToHostArray()));
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void DivRemModModPow_RandomLargeOperands_MatchesBigIntegerReference(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 3, engine);

        var arrA = GenerateRandomArray(TestSizeWords, 60);
        var arrB = GenerateRandomArray(TestSizeWords, 61);

        // ensure b is smaller but non-zero
        arrB[TestSizeWords - 1] &= 0x7FFFFFFF;
        if (arrB[0] == 0) arrB[0] = 1;

        var bigA = ToBigInteger(arrA);
        var bigB = ToBigInteger(arrB);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);

        using var gpuQ = new GpuBigUInt(context, TestSizeWords);
        using var gpuR = new GpuBigUInt(context, TestSizeWords);

        // DivRem
        GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
        AssertGpuResultEqualsCpu(bigA / bigB, gpuQ);
        AssertGpuResultEqualsCpu(bigA % bigB, gpuR);

        // Mod
        using var gpuMod = new GpuBigUInt(context, TestSizeWords);
        GpuBigIntDivRem.Mod(gpuA, gpuB, gpuMod);
        AssertGpuResultEqualsCpu(bigA % bigB, gpuMod);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void DivRem_SmallerEqualPowerOfTwoAndZeroDividend_ReturnsQuotientAndRemainder(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 3, engine);

        var arrA = new uint[TestSizeWords];
        var arrB = new uint[TestSizeWords];
        arrA[0] = 5;
        arrB[0] = 10;

        // Case 1: a < b
        using (var gpuA = new GpuBigUInt(context, arrA))
        using (var gpuB = new GpuBigUInt(context, arrB))
        using (var gpuQ = new GpuBigUInt(context, TestSizeWords))
        using (var gpuR = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
            AssertGpuResultEqualsCpu(0, gpuQ);
            AssertGpuResultEqualsCpu(5, gpuR);
        }

        // Case 2: a == b
        arrA[0] = 10;
        using (var gpuA = new GpuBigUInt(context, arrA))
        using (var gpuB = new GpuBigUInt(context, arrB))
        using (var gpuQ = new GpuBigUInt(context, TestSizeWords))
        using (var gpuR = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
            AssertGpuResultEqualsCpu(1, gpuQ);
            AssertGpuResultEqualsCpu(0, gpuR);
        }

        // Case 3: b is a power of 2
        arrA[0] = 100;
        arrB[0] = 16;
        using (var gpuA = new GpuBigUInt(context, arrA))
        using (var gpuB = new GpuBigUInt(context, arrB))
        using (var gpuQ = new GpuBigUInt(context, TestSizeWords))
        using (var gpuR = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
            AssertGpuResultEqualsCpu(6, gpuQ);
            AssertGpuResultEqualsCpu(4, gpuR);
        }

        // Case 4: a = 0
        arrA[0] = 0;
        using (var gpuA = new GpuBigUInt(context, arrA))
        using (var gpuB = new GpuBigUInt(context, arrB))
        using (var gpuQ = new GpuBigUInt(context, TestSizeWords))
        using (var gpuR = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
            AssertGpuResultEqualsCpu(0, gpuQ);
            AssertGpuResultEqualsCpu(0, gpuR);
        }

        // Case 5: Divide by zero (throws)
        // CUDA Sticky error prevention: intentionally crashing kernel causes CudaErrorLaunchFailed for other tests
        arrB[0] = 0;
        using (var gpuA = new GpuBigUInt(context, arrA))
        using (var gpuB = new GpuBigUInt(context, arrB))
        using (var gpuQ = new GpuBigUInt(context, TestSizeWords))
        using (var gpuR = new GpuBigUInt(context, TestSizeWords))
        {
            Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR));
        }
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Square_RandomLargeOperands_MatchesBigIntegerReference(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 4, engine);
        var arrA = GenerateRandomArray(TestSizeWords, 90);
        var bigA = ToBigInteger(arrA);

        using var gpuA = new GpuBigUInt(context, TestSizeWords * 4);
        using var tempA = new GpuBigUInt(context, arrA);
        gpuA.CopyFrom(tempA);
        // Square reports success. mutant: KernelBasicMathHandler.cs:368 `return true` -> `return false`
        Assert.True(gpuA.Square(TestSizeWords));
        var actual = ToBigInteger(gpuA.ToHostArray().Take(TestSizeWords * 2).ToArray());
        var expected = bigA * bigA;

        var mask = (BigInteger.One << (TestSizeWords * 2 * 32)) - 1;
        expected &= mask;

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void ModInverse_CoprimeOperands_MatchesBigIntegerReference(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);

        // Find a coprime pair (a, m)
        BigInteger bigA, bigM;
        uint[] arrA, arrM;
        do
        {
            arrA = GenerateRandomArray(TestSizeWords, 60);
            arrM = GenerateRandomArray(TestSizeWords, 61);
            // Ensure m > 1 and a > 0
            arrM[0] |= 3;
            arrA[0] |= 1;
            bigA = ToBigInteger(arrA);
            bigM = ToBigInteger(arrM);
        } while (BigInteger.GreatestCommonDivisor(bigA, bigM) != 1);

        var expectedInverse = ModInverseCpu(bigA, bigM);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuM = new GpuBigUInt(context, arrM);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        GpuBigIntMath.ModInverse(gpuA, gpuM, gpuResult);
        var actualInverseMath = ToBigInteger(gpuResult.ToHostArray());
        Assert.Equal(expectedInverse, actualInverseMath);

        using var gpuResult2 = gpuA.ModInverse(gpuM);
        var actualInverseStruct = ToBigInteger(gpuResult2.ToHostArray());
        Assert.Equal(expectedInverse, actualInverseStruct);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void ModInverse_NotCoprimeOperands_ThrowsArithmeticException(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 2, engine);

        // Create a non-coprime pair (e.g., both even)
        var arrA = GenerateRandomArray(TestSizeWords, 62);
        var arrM = GenerateRandomArray(TestSizeWords, 63);
        arrA[0] &= ~1u; // Make even
        arrM[0] &= ~1u; // Make even
        arrA[0] |= 2u;
        arrM[0] |= 2u;

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuM = new GpuBigUInt(context, arrM);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        Assert.Throws<ArithmeticException>(() => GpuBigIntMath.ModInverse(gpuA, gpuM, gpuResult));
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Sqrt_RandomLargeOperands_MatchesBigIntegerReference(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        var sizeWords = 16;
        using var context = new GpuWorkspace(sizeWords * 8, engine);

        var arrA = GenerateRandomArray(sizeWords, 123);
        // Ensure A is fairly large so we can test convergence well
        arrA[sizeWords - 1] |= 0x40000000;

        var bigA = ToBigInteger(arrA);
        var expectedSqrt = bigA.Sqrt();

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuResult = new GpuBigUInt(context, sizeWords);

        gpuResult.Value = GpuBigUIntExpr.Sqrt(gpuA);

        var resultArr = gpuResult.ToHostArray();
        var actualBig = ToBigInteger(resultArr);

        Assert.Equal(expectedSqrt, actualBig);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void Sqrt_ZeroAndExtrema_MatchesExpectedRoots(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 4, engine);
        var arrA = new uint[TestSizeWords];

        // Case 1: a = 0
        arrA[0] = 0;
        using (var gpuA = new GpuBigUInt(context, arrA))
        {
            using var gpuResult = new GpuBigUInt(context, TestSizeWords);
            gpuResult.Value = GpuBigUIntExpr.Sqrt(gpuA);
            AssertGpuResultEqualsCpu(0, gpuResult);
        }

        // Case 2: a = 1
        arrA[0] = 1;
        using (var gpuA = new GpuBigUInt(context, arrA))
        {
            using var gpuResult = new GpuBigUInt(context, TestSizeWords);
            gpuResult.Value = GpuBigUIntExpr.Sqrt(gpuA);
            AssertGpuResultEqualsCpu(1, gpuResult);
        }

        // Case 3: a = 16 (Power of 2)
        arrA[0] = 16;
        using (var gpuA = new GpuBigUInt(context, arrA))
        {
            using var gpuResult = new GpuBigUInt(context, TestSizeWords);
            gpuResult.Value = GpuBigUIntExpr.Sqrt(gpuA);
            AssertGpuResultEqualsCpu(4, gpuResult);
        }
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void ModPow_RandomLargeOperands_MatchesBigIntegerReference(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 13, engine);

        var arrBase = GenerateRandomArray(TestSizeWords, 800);
        var arrExp = GenerateRandomArray(1, 801); // Very small exponent for testing speed
        var arrMod = GenerateRandomArray(TestSizeWords, 802);
        arrMod[TestSizeWords - 1] &= 0x7FFFFFFF;
        if (arrMod[0] == 0) arrMod[0] = 1;

        var bigBase = ToBigInteger(arrBase);
        var bigExp = ToBigInteger(arrExp);
        var bigMod = ToBigInteger(arrMod);

        var expected = BigInteger.ModPow(bigBase, bigExp, bigMod);

        using var gpuBase = new GpuBigUInt(context, arrBase);
        var arrExpPadded = new uint[TestSizeWords];
        Array.Copy(arrExp, arrExpPadded, arrExp.Length);
        using var gpuExp = new GpuBigUInt(context, arrExpPadded);
        using var gpuMod = new GpuBigUInt(context, arrMod);
        using var gpuResult = new GpuBigUInt(context, TestSizeWords);

        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        var actual = ToBigInteger(gpuResult.ToHostArray());

        Assert.Equal(expected, actual);
    }

    [SkippableTheory]
    [InlineData(GpuExecutionEngine.GSharpAot)]
    public void ModPow_ModulusOneZeroBaseAndZeroExponent_ReturnsExpectedResult(GpuExecutionEngine engine)
    {
        CudaCapability.EnsureGpuAvailable();
        using var context = new GpuWorkspace(TestSizeWords * 13, engine);
        var arrBase = new uint[TestSizeWords];
        var arrExp = new uint[TestSizeWords];
        var arrMod = new uint[TestSizeWords];

        // Case 1: Mod = 1
        arrBase[0] = 5;
        arrExp[0] = 3;
        arrMod[0] = 1;
        using (var gpuBase = new GpuBigUInt(context, arrBase))
        using (var gpuExp = new GpuBigUInt(context, arrExp))
        using (var gpuMod = new GpuBigUInt(context, arrMod))
        using (var gpuResult = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
            AssertGpuResultEqualsCpu(0, gpuResult);
        }

        // Case 2: Base = 0, Exp > 0
        arrBase[0] = 0;
        arrExp[0] = 5;
        arrMod[0] = 7;
        using (var gpuBase = new GpuBigUInt(context, arrBase))
        using (var gpuExp = new GpuBigUInt(context, arrExp))
        using (var gpuMod = new GpuBigUInt(context, arrMod))
        using (var gpuResult = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
            AssertGpuResultEqualsCpu(0, gpuResult);
        }

        // Case 3: Exp = 0
        arrBase[0] = 5;
        arrExp[0] = 0;
        arrMod[0] = 7;
        using (var gpuBase = new GpuBigUInt(context, arrBase))
        using (var gpuExp = new GpuBigUInt(context, arrExp))
        using (var gpuMod = new GpuBigUInt(context, arrMod))
        using (var gpuResult = new GpuBigUInt(context, TestSizeWords))
        {
            GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
            AssertGpuResultEqualsCpu(1, gpuResult);
        }

        // Case 4: Divide by zero (Mod = 0)
        // CUDA Sticky error prevention: intentionally crashing kernel causes CudaErrorLaunchFailed for other tests
        arrMod[0] = 0;
        using (var gpuBase = new GpuBigUInt(context, arrBase))
        using (var gpuExp = new GpuBigUInt(context, arrExp))
        using (var gpuMod = new GpuBigUInt(context, arrMod))
        using (var gpuResult = new GpuBigUInt(context, TestSizeWords))
        {
            Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult));
        }
    }
}
