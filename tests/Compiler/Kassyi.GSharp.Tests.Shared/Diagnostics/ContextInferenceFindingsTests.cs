using System.Collections.Concurrent;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

[Category(TestCategory.Compiler)]
public class ContextInferenceFindingsTests
{
    private const string KnownBugSkipReason = "ContextInferenceEngine misses ThreadIndex reads and caches false in cycles (#476)";

    private static (IMethodSymbol Symbol, Compilation Compilation) GetMethod(
        string name,
        [System.Runtime.CompilerServices.CallerFilePath] string currentFile = "")
    {
        var (model, symbol) = RoslynTestUtility.GetMethodContext<ContextInferenceFindingsClass>(name, currentFile);
        return (symbol, model.Compilation);
    }

    private static ConcurrentDictionary<IMethodSymbol, bool> NewCache() => new(SymbolEqualityComparer.Default);

    [Fact(Skip = KnownBugSkipReason)]
    [Scenario(TestScenario.SadPath)]
    public void IsParallelDependent_ThreadIndexRead_ReturnsTrue()
    {
        var (symbol, compilation) = GetMethod(nameof(ContextInferenceFindingsClass.ReadsThreadIndex));

        Assert.True(ContextInferenceEngine.IsParallelDependent(symbol, NewCache(), compilation));
    }

    [Fact(Skip = KnownBugSkipReason)]
    [Scenario(TestScenario.SadPath)]
    public void IsParallelDependent_CycleMember_IsNotCachedAsFalseBeforeTheCycleIsResolved()
    {
        var (first, compilation) = GetMethod(nameof(ContextInferenceFindingsClass.CallsFirst));
        var (second, _) = GetMethod(nameof(ContextInferenceFindingsClass.CallsSecond));
        var cache = NewCache();

        Assert.True(ContextInferenceEngine.IsParallelDependent(first, cache, compilation));
        // CallsSecond calls CallsFirst, which reaches Gpu.SyncGrid.
        Assert.True(ContextInferenceEngine.IsParallelDependent(second, cache, compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsParallelDependent_CycleMember_WithFreshCache_ReturnsTrue()
    {
        var (second, compilation) = GetMethod(nameof(ContextInferenceFindingsClass.CallsSecond));

        Assert.True(ContextInferenceEngine.IsParallelDependent(second, NewCache(), compilation));
    }
}
