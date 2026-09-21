using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases;

public static class GEN_57_InOutArrayMarshaling
{
    [GpuKernel]
    public static void InOutKernel([In, Out] float[] data, int n)
    {
        int idx = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (idx < n)
        {
            data[idx] *= 2.0f;
        }
    }
}
