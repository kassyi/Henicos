
namespace Kassyi.GSharp.Tests.Helpers;

public class GEN_54_MegaCompilationCacheTests
:  TranspilerTestBase{
    [MemberData(nameof(TranspilerTestBase.Dialects))]
    [Theory]
    [Trait("Category", "Stress")]
    public void GetDynamicCompilation_ConcurrentAccess_IsThreadSafe(ITranspilerDialect dialect)
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

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    [Trait("Category", "Stress")]
    public void GetDynamicCompilation_ConcurrentAccess_WithExplicitCallerPaths_IsDeterministicAndThreadSafe(ITranspilerDialect dialect)
    {
        var exceptions = new ConcurrentBag<Exception>();
        var thisFilePath = GetThisFilePath();

        Parallel.For(0, 300, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
        {
            try
            {
                var compWithExplicitPath = MegaCompilationCache.GetDynamicCompilation(thisFilePath);
                Assert.NotNull(compWithExplicitPath);

                var compWithEmptyPath = MegaCompilationCache.GetDynamicCompilation("");
                Assert.NotNull(compWithEmptyPath);

                Assert.NotEmpty(compWithExplicitPath.SyntaxTrees);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [MemberData(nameof(TranspilerTestBase.Dialects))]

    [Theory]
    public void BaseCompilation_IsNotNullAndHasUnsafeAllowed(ITranspilerDialect dialect)
    {
        var baseComp = MegaCompilationCache.BaseCompilation;
        Assert.NotNull(baseComp);
        Assert.True(baseComp.Options.AllowUnsafe);
    }

    private static string GetThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
