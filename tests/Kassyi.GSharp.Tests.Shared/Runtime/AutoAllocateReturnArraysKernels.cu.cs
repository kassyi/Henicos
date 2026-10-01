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
}
