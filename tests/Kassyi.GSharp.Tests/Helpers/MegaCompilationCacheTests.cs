using System.Collections.Concurrent;
using Xunit;

namespace Kassyi.GSharp.Tests.Helpers;

public class MegaCompilationCacheTests
{
    [Fact]
    [Trait("Category", "Stress")]
    public void GetDynamicCompilation_ConcurrentAccess_IsThreadSafe()
    {
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
        {
            try
            {
                var compilation = MegaCompilationCache.GetDynamicCompilation();
                Assert.NotNull(compilation);

                // Call with callerFilePath
                var dynamicComp = MegaCompilationCache.GetDynamicCompilation();
                Assert.NotNull(dynamicComp);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [Fact]
    public void BaseCompilation_IsNotNullAndHasUnsafeAllowed()
    {
        var baseComp = MegaCompilationCache.BaseCompilation;
        Assert.NotNull(baseComp);
        Assert.True(baseComp.Options.AllowUnsafe);
    }
}
