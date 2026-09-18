using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN_154_WarpShuffleSadPath
{
    // Calling ShuffleDownSync in host-side non-GPU kernel context
    public static void HostCallToShuffleDownSync()
    {
        uint mask = 0xffffffff;
        int val = 10;
        int res = Gpu.ShuffleDownSync(mask, val, 1);
    }
}
