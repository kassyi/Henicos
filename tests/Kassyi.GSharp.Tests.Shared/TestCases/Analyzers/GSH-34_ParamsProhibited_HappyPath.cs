using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public static class GSH_34_ParamsProhibited_HappyPath
{
    [GpuKernel]
    public static unsafe void KernelWithoutParams(int* items, int length)
    {
    }

    [GpuDevice]
    public static int DeviceMethodWithoutParams(int baseVal, int modifier)
    {
        return baseVal + modifier;
    }

    public static void HostMethodWithParams(params int[] args)
    {
    }
}
