using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public struct @union
{
    public int V;
}

public struct @closed
{
    public int W;
}

public static partial class ContextualKeywordTypeKernels
{
    [GpuKernel]
    public static void KeywordTypesKernel(int[] o)
    {
        int t = ThreadIndex.X;
        @union u = new @union { V = t };
        @closed c = new @closed { W = 3 };
        Gpu.SyncThreads();
        o[t] = u.V * c.W + 1;
    }
}
