using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public unsafe partial class AutoAllocateReturnArraysKernels
{
    [GpuKernel]
    public static float NormalReturnKernel(int someValue)
    {
        return someValue * 2.0f;
    }

    [GpuKernel]
    public static int OneThreadKernel()
    {
        return 42;
    }

    public struct TestStruct
    {
        public int A;
        public float B;
    }

    [GpuKernel]
    public static TestStruct StructReturnKernel()
    {
        TestStruct s;
        s.A = ThreadIndex.X;
        s.B = ThreadIndex.X * 1.5f;
        return s;
    }

    // Vector2 and Vector4 are 8- and 16-byte aligned on CUDA, 4-byte aligned in .NET: V, B and W sit at other offsets
    // on the device, so the launcher converts each returned element (#704)
    public struct VectorResult
    {
        public float A;
        public Vector2 V;
        public float B;
        public Vector4 W;
    }

    [GpuKernel]
    public static VectorResult VectorResultKernel(float scale)
    {
        float t = ThreadIndex.X * scale;
        VectorResult r;
        r.A = t + 1f;
        r.V = new Vector2(t + 2f, t + 3f);
        r.B = t + 4f;
        r.W = new Vector4(t + 5f, t + 6f, t + 7f, t + 8f);
        return r;
    }

    // The runtime may reorder the fields of an auto-layout struct, so both dialects convert each returned element
    [StructLayout(LayoutKind.Auto)]
    public struct AutoLayoutResult
    {
        public byte A;
        public long B;
        public byte C;
    }

    [GpuKernel]
    public static AutoLayoutResult AutoLayoutResultKernel()
    {
        int i = ThreadIndex.X;
        AutoLayoutResult r;
        r.A = (byte)(i + 1);
        r.B = 1000L * i + 7L;
        r.C = (byte)(i + 100);
        return r;
    }
}
