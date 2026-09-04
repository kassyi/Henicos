using System.Numerics;

namespace Kassyi.GSharp.Tests.Syntax;

public class GEN_18_VectorBuiltinTests
:  TranspilerTestBase{
    [Theory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GEN-18")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void GEN18_TestVector2AddKernel(int arrayLength)
    {
        System.Numerics.Vector2[] a = new System.Numerics.Vector2[arrayLength];
        System.Numerics.Vector2[] b = new System.Numerics.Vector2[arrayLength];
        System.Numerics.Vector2[] expected = new System.Numerics.Vector2[arrayLength];

        for (int i = 0; i < arrayLength; i++)
        {
            a[i] = new System.Numerics.Vector2((float)i, (float)(i * 2));
            b[i] = new System.Numerics.Vector2((float)(i * 3), (float)(i * 4));
            expected[i] = new System.Numerics.Vector2((float)(i + i * 3), (float)(i * 2 + i * 4));
        }

        System.Numerics.Vector2[] result = new System.Numerics.Vector2[arrayLength];

        GEN_18_VectorBuiltinKernelsWrapper.Launch_Vector2AddKernel(a, b, result, arrayLength, blockDimX: 32);

        for (int i = 0; i < arrayLength; i++)
        {
            Assert.Equal(expected[i].X, result[i].X, 5);
            Assert.Equal(expected[i].Y, result[i].Y, 5);
        }
    }

    [Theory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GEN-18")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void GEN18_TestVector3AddKernel(int arrayLength)
    {
        System.Numerics.Vector3[] a = new System.Numerics.Vector3[arrayLength];
        System.Numerics.Vector3[] b = new System.Numerics.Vector3[arrayLength];
        System.Numerics.Vector3[] expected = new System.Numerics.Vector3[arrayLength];

        for (int i = 0; i < arrayLength; i++)
        {
            a[i] = new System.Numerics.Vector3((float)i, (float)(i * 2), (float)(i * 3));
            b[i] = new System.Numerics.Vector3((float)(i * 4), (float)(i * 5), (float)(i * 6));
            expected[i] = new System.Numerics.Vector3(a[i].X + b[i].X, a[i].Y + b[i].Y, a[i].Z + b[i].Z);
        }

        System.Numerics.Vector3[] result = new System.Numerics.Vector3[arrayLength];

        GEN_18_VectorBuiltinKernelsWrapper.Launch_Vector3AddKernel(a, b, result, arrayLength, blockDimX: 32);

        for (int i = 0; i < arrayLength; i++)
        {
            Assert.Equal(expected[i].X, result[i].X, 5);
            Assert.Equal(expected[i].Y, result[i].Y, 5);
            Assert.Equal(expected[i].Z, result[i].Z, 5);
        }
    }

    [Theory]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GEN-18")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void GEN18_TestVector4AddKernel(int arrayLength)
    {
        System.Numerics.Vector4[] a = new System.Numerics.Vector4[arrayLength];
        System.Numerics.Vector4[] b = new System.Numerics.Vector4[arrayLength];
        System.Numerics.Vector4[] expected = new System.Numerics.Vector4[arrayLength];

        for (int i = 0; i < arrayLength; i++)
        {
            a[i] = new System.Numerics.Vector4((float)i, (float)(i * 2), (float)(i * 3), (float)(i * 4));
            b[i] = new System.Numerics.Vector4((float)(i * 5), (float)(i * 6), (float)(i * 7), (float)(i * 8));
            expected[i] = new System.Numerics.Vector4(a[i].X + b[i].X, a[i].Y + b[i].Y, a[i].Z + b[i].Z, a[i].W + b[i].W);
        }

        System.Numerics.Vector4[] result = new System.Numerics.Vector4[arrayLength];

        GEN_18_VectorBuiltinKernelsWrapper.Launch_Vector4AddKernel(a, b, result, arrayLength, blockDimX: 32);

        for (int i = 0; i < arrayLength; i++)
        {
            Assert.Equal(expected[i].X, result[i].X, 5);
            Assert.Equal(expected[i].Y, result[i].Y, 5);
            Assert.Equal(expected[i].Z, result[i].Z, 5);
            Assert.Equal(expected[i].W, result[i].W, 5);
        }
    }
}
