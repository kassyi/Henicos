// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class ContractsAndPtxKernels
{
    [GpuKernel]
    public static void InlinePtxKernel(uint[] data)
    {
        Gpu.InlinePtx("data[0] = 999;");
    }
}
