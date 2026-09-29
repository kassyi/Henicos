using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static partial class VectorBuiltinKernels
{
    [GpuKernel]
    public static void Vector2AddKernel(System.Numerics.Vector2[] a, System.Numerics.Vector2[] b, System.Numerics.Vector2[] result, int length)
    {
        var tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            var va = a[tid];
            var vb = b[tid];
            result[tid] = new System.Numerics.Vector2((float)(va.X + vb.X), (float)(va.Y + vb.Y));
        }
    }

    [GpuKernel]
    public static void Vector3AddKernel(System.Numerics.Vector3[] a, System.Numerics.Vector3[] b, System.Numerics.Vector3[] result, int length)
    {
        var tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            var va = a[tid];
            var vb = b[tid];
            result[tid] = new System.Numerics.Vector3((float)(va.X + vb.X), (float)(va.Y + vb.Y), (float)(va.Z + vb.Z));
        }
    }

    [GpuKernel]
    public static void Vector4AddKernel(System.Numerics.Vector4[] a, System.Numerics.Vector4[] b, System.Numerics.Vector4[] result, int length)
    {
        var tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            var va = a[tid];
            var vb = b[tid];
            result[tid] = new System.Numerics.Vector4((float)(va.X + vb.X), (float)(va.Y + vb.Y), (float)(va.Z + vb.Z), (float)(va.W + vb.W));
        }
    }
}
