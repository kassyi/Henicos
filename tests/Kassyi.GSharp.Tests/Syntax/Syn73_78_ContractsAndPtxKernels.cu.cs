using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public static class Syn73_78_ContractsAndPtxKernels
{
    [GpuKernel]
    public static void InlinePtxKernel(uint[] data)
    {
        Gpu.InlinePtx("data[0] = 999;");
    }
}
