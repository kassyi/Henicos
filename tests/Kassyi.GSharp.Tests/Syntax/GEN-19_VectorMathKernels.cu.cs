namespace Kassyi.GSharp.Tests.Syntax;

public static partial class GEN_19_VectorMathKernels
{
    [GpuKernel]
    public static void Vector2ArithmeticKernel(System.Numerics.Vector2[] a, System.Numerics.Vector2[] b, float scalar, System.Numerics.Vector2[] addRes, System.Numerics.Vector2[] subRes, System.Numerics.Vector2[] mulRes, System.Numerics.Vector2[] divRes, System.Numerics.Vector2[] scalarMulRes, System.Numerics.Vector2[] scalarDivRes, int length)
    {
        var tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            var va = a[tid];
            var vb = b[tid];
            addRes[tid] = va + vb;
            subRes[tid] = va - vb;
            mulRes[tid] = va * vb;
            divRes[tid] = va / vb;
            scalarMulRes[tid] = va * scalar;
            scalarDivRes[tid] = va / scalar;
        }
    }

    [GpuKernel]
    public static void Vector3ArithmeticKernel(System.Numerics.Vector3[] a, System.Numerics.Vector3[] b, float scalar, System.Numerics.Vector3[] addRes, System.Numerics.Vector3[] subRes, System.Numerics.Vector3[] mulRes, System.Numerics.Vector3[] divRes, System.Numerics.Vector3[] scalarMulRes, System.Numerics.Vector3[] scalarDivRes, int length)
    {
        var tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            var va = a[tid];
            var vb = b[tid];
            addRes[tid] = va + vb;
            subRes[tid] = va - vb;
            mulRes[tid] = va * vb;
            divRes[tid] = va / vb;
            scalarMulRes[tid] = va * scalar;
            scalarDivRes[tid] = va / scalar;
        }
    }

    [GpuKernel]
    public static void Vector4ArithmeticKernel(System.Numerics.Vector4[] a, System.Numerics.Vector4[] b, float scalar, System.Numerics.Vector4[] addRes, System.Numerics.Vector4[] subRes, System.Numerics.Vector4[] mulRes, System.Numerics.Vector4[] divRes, System.Numerics.Vector4[] scalarMulRes, System.Numerics.Vector4[] scalarDivRes, int length)
    {
        var tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            var va = a[tid];
            var vb = b[tid];
            addRes[tid] = va + vb;
            subRes[tid] = va - vb;
            mulRes[tid] = va * vb;
            divRes[tid] = va / vb;
            scalarMulRes[tid] = va * scalar;
            scalarDivRes[tid] = va / scalar;
        }
    }

    [GpuKernel]
    public static void Vector2BuiltinsKernel(System.Numerics.Vector2[] a, System.Numerics.Vector2[] b, float[] dotRes, float[] distRes, System.Numerics.Vector2[] normRes, System.Numerics.Vector2[] sqrtRes, float[] lenRes, int length)
    {
        var tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            var va = a[tid];
            var vb = b[tid];
            dotRes[tid] = System.Numerics.Vector2.Dot(va, vb);
            distRes[tid] = System.Numerics.Vector2.Distance(va, vb);
            normRes[tid] = System.Numerics.Vector2.Normalize(va);
            sqrtRes[tid] = System.Numerics.Vector2.SquareRoot(va);
            lenRes[tid] = va.Length();
        }
    }

    [GpuKernel]
    public static void Vector3And4BuiltinsKernel(System.Numerics.Vector3[] v3a, System.Numerics.Vector3[] v3b, float[] v3Dot, float[] v3Dist, System.Numerics.Vector3[] v3Norm, float[] v3Len, System.Numerics.Vector4[] v4a, System.Numerics.Vector4[] v4b, float[] v4Dot, float[] v4Dist, System.Numerics.Vector4[] v4Norm, float[] v4Len, int length)
    {
        var tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            var a3 = v3a[tid];
            var b3 = v3b[tid];
            v3Dot[tid] = System.Numerics.Vector3.Dot(a3, b3);
            v3Dist[tid] = System.Numerics.Vector3.Distance(a3, b3);
            v3Norm[tid] = System.Numerics.Vector3.Normalize(a3);
            v3Len[tid] = a3.Length();

            var a4 = v4a[tid];
            var b4 = v4b[tid];
            v4Dot[tid] = System.Numerics.Vector4.Dot(a4, b4);
            v4Dist[tid] = System.Numerics.Vector4.Distance(a4, b4);
            v4Norm[tid] = System.Numerics.Vector4.Normalize(a4);
            v4Len[tid] = a4.Length();
        }
    }

    [GpuKernel]
    public static void Vector2NormalizeZeroKernel(System.Numerics.Vector2[] zeroVec, System.Numerics.Vector2[] normResult, int length)
    {
        var tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            normResult[tid] = System.Numerics.Vector2.Normalize(zeroVec[tid]);
        }
    }

    [GpuKernel]
    public static void VectorThreadSafetyKernel(System.Numerics.Vector2[] input, System.Numerics.Vector2[] output, int length)
    {
        var tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            System.Numerics.Vector2 local = input[tid];
            for (int i = 0; i < 10; i++)
            {
                local = System.Numerics.Vector2.Normalize(local + new System.Numerics.Vector2(1.0f, 0.5f)) * 2.0f;
            }
            output[tid] = local;
        }
    }
}
