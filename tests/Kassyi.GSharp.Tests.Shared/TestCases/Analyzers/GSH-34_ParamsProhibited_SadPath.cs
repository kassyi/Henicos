using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public static class GSH_34_ParamsProhibited_SadPath
{
    [GpuKernel]
    public static void KernelWithParams(params int[] items)
    {
    }

    [GpuDevice]
    public static int DeviceMethodWithParams(int baseVal, params int[] modifiers)
    {
        return baseVal;
    }
}
