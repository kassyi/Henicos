using System.Collections.Concurrent;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;

namespace Kassyi.GSharp.Stdlib.Math.Tests.BigInt.Providers;

[Trait("Category", "GpuExecution")]
public class GBI32_GSharpJitProviderTests
{
    [Fact]
    [Trait("FeatureId", "GBI-32")]
    public void Test_GBI32_ClearCaches_ShouldEmptyInternalPools()
    {
        var provider = (GSharpJitProvider)Activator.CreateInstance(typeof(GSharpJitProvider), nonPublic: true)!;

        // Use reflection to inspect the private collections
        var carryPoolField = typeof(GSharpJitProvider).GetField("_carryBufferPool",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var twiddleCacheField = typeof(GSharpJitProvider).GetField("_twiddleCache",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(carryPoolField);
        Assert.NotNull(twiddleCacheField);

        var carryPool = (ConcurrentDictionary<int, ConcurrentBag<IntPtr>>)carryPoolField.GetValue(provider)!;
        var twiddleCache = twiddleCacheField.GetValue(provider)! as System.Collections.IDictionary;
        // Act
        provider.ClearCaches();

        // Assert
        Assert.Empty(carryPool);
        Assert.Empty(twiddleCache!);
    }
}
