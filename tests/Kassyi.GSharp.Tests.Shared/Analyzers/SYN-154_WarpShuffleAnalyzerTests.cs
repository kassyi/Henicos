using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Analyzers;

public class SYN_154_WarpShuffleAnalyzerTests : TranspilerTestBase
{
    private static void HostCallToShuffleDownSync()
    {
        uint mask = 0xffffffff;
        int val = 10;
        Gpu.ShuffleDownSync(mask, val, 1);
    }

    [Fact]
    [Trait("Category", "SadPath")]
    [Trait("FeatureId", "154")]
    [Trait("FeatureId", "SYN-154")]
    public void Test_154_HostCallToShuffleDownSync_ThrowsGpuOnlyException()
    {
        Assert.Throws<GpuOnlyException>(() => HostCallToShuffleDownSync());
    }

}
