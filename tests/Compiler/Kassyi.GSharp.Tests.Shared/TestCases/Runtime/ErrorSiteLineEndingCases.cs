// StackLinq terminals on several lines: their error site IDs must not depend on the line endings of the checkout
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace ErrorSiteLineEndingCases;

public static class ErrorSiteLineEndingKernels
{
    [GpuKernel]
    public static void SitesOnSeveralLinesKernel(int[] data, int[] result)
    {
        result[0] = data.AsStackEnumerable().ElementAt(1);

        result[1] = data.AsStackEnumerable().First();

        result[2] = data
            .AsStackEnumerable()
            .Last();
    }
}
