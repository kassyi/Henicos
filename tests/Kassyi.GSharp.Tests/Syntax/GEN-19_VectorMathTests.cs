namespace Kassyi.GSharp.Tests.Syntax;

public class GEN_19_VectorMathTests
{
    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GEN-19")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public void Test_GEN19_Vector2Arithmetic(int length)
    {
        System.Numerics.Vector2[] a = new System.Numerics.Vector2[length];
        System.Numerics.Vector2[] b = new System.Numerics.Vector2[length];
        float scalar = 2.5f;

        System.Numerics.Vector2[] addRes = new System.Numerics.Vector2[length];
        System.Numerics.Vector2[] subRes = new System.Numerics.Vector2[length];
        System.Numerics.Vector2[] mulRes = new System.Numerics.Vector2[length];
        System.Numerics.Vector2[] divRes = new System.Numerics.Vector2[length];
        System.Numerics.Vector2[] scalarMulRes = new System.Numerics.Vector2[length];
        System.Numerics.Vector2[] scalarDivRes = new System.Numerics.Vector2[length];

        for (int i = 0; i < length; i++)
        {
            a[i] = new System.Numerics.Vector2(i + 1.0f, i + 2.0f);
            b[i] = new System.Numerics.Vector2(i + 3.0f, i + 4.0f);
        }

        GEN_19_VectorMathKernelsWrapper.Launch_Vector2ArithmeticKernel(
            a, b, scalar, addRes, subRes, mulRes, divRes, scalarMulRes, scalarDivRes, length, blockDimX: 32);

        for (int i = 0; i < length; i++)
        {
            var expectedAdd = a[i] + b[i];
            var expectedSub = a[i] - b[i];
            var expectedMul = a[i] * b[i];
            var expectedDiv = a[i] / b[i];
            var expectedScalarMul = a[i] * scalar;
            var expectedScalarDiv = a[i] / scalar;

            Assert.Equal(expectedAdd.X, addRes[i].X, 4);
            Assert.Equal(expectedAdd.Y, addRes[i].Y, 4);

            Assert.Equal(expectedSub.X, subRes[i].X, 4);
            Assert.Equal(expectedSub.Y, subRes[i].Y, 4);

            Assert.Equal(expectedMul.X, mulRes[i].X, 4);
            Assert.Equal(expectedMul.Y, mulRes[i].Y, 4);

            Assert.Equal(expectedDiv.X, divRes[i].X, 4);
            Assert.Equal(expectedDiv.Y, divRes[i].Y, 4);

            Assert.Equal(expectedScalarMul.X, scalarMulRes[i].X, 4);
            Assert.Equal(expectedScalarMul.Y, scalarMulRes[i].Y, 4);

            Assert.Equal(expectedScalarDiv.X, scalarDivRes[i].X, 4);
            Assert.Equal(expectedScalarDiv.Y, scalarDivRes[i].Y, 4);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GEN-19")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public void Test_GEN19_Vector3Arithmetic(int length)
    {
        System.Numerics.Vector3[] a = new System.Numerics.Vector3[length];
        System.Numerics.Vector3[] b = new System.Numerics.Vector3[length];
        float scalar = 3.0f;

        System.Numerics.Vector3[] addRes = new System.Numerics.Vector3[length];
        System.Numerics.Vector3[] subRes = new System.Numerics.Vector3[length];
        System.Numerics.Vector3[] mulRes = new System.Numerics.Vector3[length];
        System.Numerics.Vector3[] divRes = new System.Numerics.Vector3[length];
        System.Numerics.Vector3[] scalarMulRes = new System.Numerics.Vector3[length];
        System.Numerics.Vector3[] scalarDivRes = new System.Numerics.Vector3[length];

        for (int i = 0; i < length; i++)
        {
            a[i] = new System.Numerics.Vector3(i + 1.0f, i + 2.0f, i + 3.0f);
            b[i] = new System.Numerics.Vector3(i + 4.0f, i + 5.0f, i + 6.0f);
        }

        GEN_19_VectorMathKernelsWrapper.Launch_Vector3ArithmeticKernel(
            a, b, scalar, addRes, subRes, mulRes, divRes, scalarMulRes, scalarDivRes, length, blockDimX: 32);

        for (int i = 0; i < length; i++)
        {
            var expectedAdd = a[i] + b[i];
            var expectedSub = a[i] - b[i];
            var expectedMul = a[i] * b[i];
            var expectedDiv = a[i] / b[i];
            var expectedScalarMul = a[i] * scalar;
            var expectedScalarDiv = a[i] / scalar;

            Assert.Equal(expectedAdd.X, addRes[i].X, 4);
            Assert.Equal(expectedAdd.Y, addRes[i].Y, 4);
            Assert.Equal(expectedAdd.Z, addRes[i].Z, 4);

            Assert.Equal(expectedSub.X, subRes[i].X, 4);
            Assert.Equal(expectedSub.Y, subRes[i].Y, 4);
            Assert.Equal(expectedSub.Z, subRes[i].Z, 4);

            Assert.Equal(expectedMul.X, mulRes[i].X, 4);
            Assert.Equal(expectedMul.Y, mulRes[i].Y, 4);
            Assert.Equal(expectedMul.Z, mulRes[i].Z, 4);

            Assert.Equal(expectedDiv.X, divRes[i].X, 4);
            Assert.Equal(expectedDiv.Y, divRes[i].Y, 4);
            Assert.Equal(expectedDiv.Z, divRes[i].Z, 4);

            Assert.Equal(expectedScalarMul.X, scalarMulRes[i].X, 4);
            Assert.Equal(expectedScalarMul.Y, scalarMulRes[i].Y, 4);
            Assert.Equal(expectedScalarMul.Z, scalarMulRes[i].Z, 4);

            Assert.Equal(expectedScalarDiv.X, scalarDivRes[i].X, 4);
            Assert.Equal(expectedScalarDiv.Y, scalarDivRes[i].Y, 4);
            Assert.Equal(expectedScalarDiv.Z, scalarDivRes[i].Z, 4);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GEN-19")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public void Test_GEN19_Vector4Arithmetic(int length)
    {
        System.Numerics.Vector4[] a = new System.Numerics.Vector4[length];
        System.Numerics.Vector4[] b = new System.Numerics.Vector4[length];
        float scalar = 0.5f;

        System.Numerics.Vector4[] addRes = new System.Numerics.Vector4[length];
        System.Numerics.Vector4[] subRes = new System.Numerics.Vector4[length];
        System.Numerics.Vector4[] mulRes = new System.Numerics.Vector4[length];
        System.Numerics.Vector4[] divRes = new System.Numerics.Vector4[length];
        System.Numerics.Vector4[] scalarMulRes = new System.Numerics.Vector4[length];
        System.Numerics.Vector4[] scalarDivRes = new System.Numerics.Vector4[length];

        for (int i = 0; i < length; i++)
        {
            a[i] = new System.Numerics.Vector4(i + 1.0f, i + 2.0f, i + 3.0f, i + 4.0f);
            b[i] = new System.Numerics.Vector4(i + 5.0f, i + 6.0f, i + 7.0f, i + 8.0f);
        }

        GEN_19_VectorMathKernelsWrapper.Launch_Vector4ArithmeticKernel(
            a, b, scalar, addRes, subRes, mulRes, divRes, scalarMulRes, scalarDivRes, length, blockDimX: 32);

        for (int i = 0; i < length; i++)
        {
            var expectedAdd = a[i] + b[i];
            var expectedSub = a[i] - b[i];
            var expectedMul = a[i] * b[i];
            var expectedDiv = a[i] / b[i];
            var expectedScalarMul = a[i] * scalar;
            var expectedScalarDiv = a[i] / scalar;

            Assert.Equal(expectedAdd.X, addRes[i].X, 4);
            Assert.Equal(expectedAdd.Y, addRes[i].Y, 4);
            Assert.Equal(expectedAdd.Z, addRes[i].Z, 4);
            Assert.Equal(expectedAdd.W, addRes[i].W, 4);

            Assert.Equal(expectedSub.X, subRes[i].X, 4);
            Assert.Equal(expectedSub.Y, subRes[i].Y, 4);
            Assert.Equal(expectedSub.Z, subRes[i].Z, 4);
            Assert.Equal(expectedSub.W, subRes[i].W, 4);

            Assert.Equal(expectedMul.X, mulRes[i].X, 4);
            Assert.Equal(expectedMul.Y, mulRes[i].Y, 4);
            Assert.Equal(expectedMul.Z, mulRes[i].Z, 4);
            Assert.Equal(expectedMul.W, mulRes[i].W, 4);

            Assert.Equal(expectedDiv.X, divRes[i].X, 4);
            Assert.Equal(expectedDiv.Y, divRes[i].Y, 4);
            Assert.Equal(expectedDiv.Z, divRes[i].Z, 4);
            Assert.Equal(expectedDiv.W, divRes[i].W, 4);

            Assert.Equal(expectedScalarMul.X, scalarMulRes[i].X, 4);
            Assert.Equal(expectedScalarMul.Y, scalarMulRes[i].Y, 4);
            Assert.Equal(expectedScalarMul.Z, scalarMulRes[i].Z, 4);
            Assert.Equal(expectedScalarMul.W, scalarMulRes[i].W, 4);

            Assert.Equal(expectedScalarDiv.X, scalarDivRes[i].X, 4);
            Assert.Equal(expectedScalarDiv.Y, scalarDivRes[i].Y, 4);
            Assert.Equal(expectedScalarDiv.Z, scalarDivRes[i].Z, 4);
            Assert.Equal(expectedScalarDiv.W, scalarDivRes[i].W, 4);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GEN-19")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public void Test_GEN19_Vector2Builtins(int length)
    {
        System.Numerics.Vector2[] a = new System.Numerics.Vector2[length];
        System.Numerics.Vector2[] b = new System.Numerics.Vector2[length];

        float[] dotRes = new float[length];
        float[] distRes = new float[length];
        System.Numerics.Vector2[] normRes = new System.Numerics.Vector2[length];
        System.Numerics.Vector2[] sqrtRes = new System.Numerics.Vector2[length];
        float[] lenRes = new float[length];

        for (int i = 0; i < length; i++)
        {
            a[i] = new System.Numerics.Vector2(i + 1.0f, i + 2.0f);
            b[i] = new System.Numerics.Vector2(i + 3.0f, i + 4.0f);
        }

        GEN_19_VectorMathKernelsWrapper.Launch_Vector2BuiltinsKernel(
            a, b, dotRes, distRes, normRes, sqrtRes, lenRes, length, blockDimX: 32);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(System.Numerics.Vector2.Dot(a[i], b[i]), dotRes[i], 4);
            Assert.Equal(System.Numerics.Vector2.Distance(a[i], b[i]), distRes[i], 4);

            var expectedNorm = System.Numerics.Vector2.Normalize(a[i]);
            Assert.Equal(expectedNorm.X, normRes[i].X, 4);
            Assert.Equal(expectedNorm.Y, normRes[i].Y, 4);

            var expectedSqrt = System.Numerics.Vector2.SquareRoot(a[i]);
            Assert.Equal(expectedSqrt.X, sqrtRes[i].X, 4);
            Assert.Equal(expectedSqrt.Y, sqrtRes[i].Y, 4);

            Assert.Equal(a[i].Length(), lenRes[i], 4);
        }
    }

    [Theory]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GEN-19")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    public void Test_GEN19_Vector3And4Builtins(int length)
    {
        System.Numerics.Vector3[] v3a = new System.Numerics.Vector3[length];
        System.Numerics.Vector3[] v3b = new System.Numerics.Vector3[length];
        float[] v3Dot = new float[length];
        float[] v3Dist = new float[length];
        System.Numerics.Vector3[] v3Norm = new System.Numerics.Vector3[length];
        float[] v3Len = new float[length];

        System.Numerics.Vector4[] v4a = new System.Numerics.Vector4[length];
        System.Numerics.Vector4[] v4b = new System.Numerics.Vector4[length];
        float[] v4Dot = new float[length];
        float[] v4Dist = new float[length];
        System.Numerics.Vector4[] v4Norm = new System.Numerics.Vector4[length];
        float[] v4Len = new float[length];

        for (int i = 0; i < length; i++)
        {
            v3a[i] = new System.Numerics.Vector3(i + 1.0f, i + 2.0f, i + 3.0f);
            v3b[i] = new System.Numerics.Vector3(i + 4.0f, i + 5.0f, i + 6.0f);

            v4a[i] = new System.Numerics.Vector4(i + 1.0f, i + 2.0f, i + 3.0f, i + 4.0f);
            v4b[i] = new System.Numerics.Vector4(i + 5.0f, i + 6.0f, i + 7.0f, i + 8.0f);
        }

        GEN_19_VectorMathKernelsWrapper.Launch_Vector3And4BuiltinsKernel(
            v3a, v3b, v3Dot, v3Dist, v3Norm, v3Len,
            v4a, v4b, v4Dot, v4Dist, v4Norm, v4Len,
            length, blockDimX: 32);

        for (int i = 0; i < length; i++)
        {
            Assert.Equal(System.Numerics.Vector3.Dot(v3a[i], v3b[i]), v3Dot[i], 4);
            Assert.Equal(System.Numerics.Vector3.Distance(v3a[i], v3b[i]), v3Dist[i], 4);

            var exp3Norm = System.Numerics.Vector3.Normalize(v3a[i]);
            Assert.Equal(exp3Norm.X, v3Norm[i].X, 4);
            Assert.Equal(exp3Norm.Y, v3Norm[i].Y, 4);
            Assert.Equal(exp3Norm.Z, v3Norm[i].Z, 4);
            Assert.Equal(v3a[i].Length(), v3Len[i], 4);

            Assert.Equal(System.Numerics.Vector4.Dot(v4a[i], v4b[i]), v4Dot[i], 4);
            Assert.Equal(System.Numerics.Vector4.Distance(v4a[i], v4b[i]), v4Dist[i], 4);

            var exp4Norm = System.Numerics.Vector4.Normalize(v4a[i]);
            Assert.Equal(exp4Norm.X, v4Norm[i].X, 4);
            Assert.Equal(exp4Norm.Y, v4Norm[i].Y, 4);
            Assert.Equal(exp4Norm.Z, v4Norm[i].Z, 4);
            Assert.Equal(exp4Norm.W, v4Norm[i].W, 4);
            Assert.Equal(v4a[i].Length(), v4Len[i], 4);
        }
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GEN-19")]
    public void Test_GEN19_Vector2NormalizeZero_EdgeCase()
    {
        System.Numerics.Vector2[] zeroVec = [System.Numerics.Vector2.Zero];
        System.Numerics.Vector2[] normRes = [System.Numerics.Vector2.Zero];

        GEN_19_VectorMathKernelsWrapper.Launch_Vector2NormalizeZeroKernel(zeroVec, normRes, 1, blockDimX: 1);

        // Vector2.Normalize(Vector2.Zero) produces NaN on float components in standard IEEE 754 / CUDA floating point arithmetic
        Assert.True(float.IsNaN(normRes[0].X));
        Assert.True(float.IsNaN(normRes[0].Y));
    }

    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "GEN-19")]
    public void Test_GEN19_VectorRegisterPurityAndThreadSafety()
    {
        int threadCount = 64;
        System.Numerics.Vector2[] input = new System.Numerics.Vector2[threadCount];
        System.Numerics.Vector2[] output = new System.Numerics.Vector2[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            input[i] = new System.Numerics.Vector2(i * 1.5f, i * 2.5f);
        }

        GEN_19_VectorMathKernelsWrapper.Launch_VectorThreadSafetyKernel(input, output, threadCount, gridDimX: 2, blockDimX: 32);

        for (int i = 0; i < threadCount; i++)
        {
            System.Numerics.Vector2 expected = input[i];
            for (int k = 0; k < 10; k++)
            {
                expected = System.Numerics.Vector2.Normalize(expected + new System.Numerics.Vector2(1.0f, 0.5f)) * 2.0f;
            }

            Assert.Equal(expected.X, output[i].X, 4);
            Assert.Equal(expected.Y, output[i].Y, 4);
        }
    }
}
