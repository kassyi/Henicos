// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static partial class ContractsAndPtxKernels
{
    [GpuKernel]
    public static void InlinePtxKernel(uint[] data)
    {
        Gpu.InlinePtx("data[0] = 999;");
    }
}
