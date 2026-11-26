using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace TestNs;

public static partial class UnmappedBclReturnKernels
{
    [GpuKernel]
    public static DateTime UnmappedReturnKernel(DateTime dt, Guid id, Matrix4x4 mat)
    {
        return dt;
    }
}
