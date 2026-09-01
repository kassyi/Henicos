using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN03_DuplicateOutVarCase
{
    [GpuDeviceFunction]
    public static bool TryCompute(int x, out int result)
    {
        result = x * 2;
        return true;
    }

    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void KernelWithDuplicateOutVar(int[] data)
    {
        int val = 10;
        // Sad Path: Redeclaring 'val' in same scope using inline out var
        if (TryCompute(data[0], out int val))
        {
            data[0] = val;
        }
    }
}
