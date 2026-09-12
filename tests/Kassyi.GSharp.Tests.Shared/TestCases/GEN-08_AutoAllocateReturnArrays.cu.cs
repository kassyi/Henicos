using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases
{
    public unsafe class AutoAllocateReturnTest
    {
        [GpuKernel]
        public static float ComputeMag(float* a) 
        {
            int tid = ThreadIndex.X;
            return a[tid] * 2.0f;
        }

        [GpuKernel]
        public static int ComputeInt(int* a, int b) 
        {
            return a[0] + b;
        }
    }
}
