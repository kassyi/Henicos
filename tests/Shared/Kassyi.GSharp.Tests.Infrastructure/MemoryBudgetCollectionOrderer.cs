#nullable enable
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Orders test collections as xUnit does by default, but puts the final memory budget check
/// (<see cref="MemoryBudget.FinalCollection"/>) last. <c>tests/Directory.Build.props</c> applies it to every test
/// assembly.
/// </summary>
/// <remarks>
/// The final collection is declared with <c>DisableParallelization = true</c>, so xUnit v2 runs it after every parallel
/// collection has finished; the non-parallel collections then run one after another in this order, which puts the
/// final check after <c>DeviceExclusive</c> and the other sequential collections. Without test parallelization every
/// collection runs in this order.
/// </remarks>
public sealed class MemoryBudgetCollectionOrderer : ITestCollectionOrderer
{
    private readonly DefaultTestCollectionOrderer _default = new();

    /// <inheritdoc/>
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        var ordered = new List<ITestCollection>(_default.OrderTestCollections(testCollections));
        int final = ordered.FindIndex(static c => c.DisplayName == MemoryBudget.FinalCollection);
        if (final >= 0)
        {
            ITestCollection collection = ordered[final];
            ordered.RemoveAt(final);
            ordered.Add(collection);
        }

        return ordered;
    }
}
