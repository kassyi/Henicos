// ReSharper disable All
using Kassyi.GSharp.Core;

// On the C++ dialects these kernels test the runtime failure that replaces Gpu.InlinePtx, so its warning is expected
#pragma warning disable GSH0054

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class ContractsAndPtxKernels
{
    [GpuKernel]
    public static void InlinePtxKernel(uint[] data)
    {
        Gpu.InlinePtx($"{data}[0] = 999;");
    }

    [GpuKernel]
    public static void InlinePtxFollowedByWriteKernel(uint[] data)
    {
        Gpu.InlinePtx($"{data}[0] = 999;");
        data[0] = 777;
    }
}
