#nullable enable
using System.Runtime.CompilerServices;

// Compiled into every test assembly by tests/Directory.Build.props (not part of Kassyi.GSharp.Tests.Infrastructure):
// xUnit only discovers tests and collection definitions inside the test assembly itself.
namespace Kassyi.GSharp.Tests.Infrastructure.MemoryBudgetHooks;

internal static class MemoryBudgetModuleInitializer
{
    [ModuleInitializer]
    internal static void Start() =>
        MemoryBudget.Start(typeof(MemoryBudgetModuleInitializer).Assembly.GetName().Name ?? string.Empty);
}

/// <summary>
/// The collection of the final memory budget check. <c>DisableParallelization</c> and
/// <see cref="MemoryBudgetCollectionOrderer"/> make it run after every other collection.
/// </summary>
[CollectionDefinition(MemoryBudget.FinalCollection, DisableParallelization = true)]
public sealed class MemoryBudgetFinalCollection;

/// <summary>
/// Fails when the test host exceeded its memory budget at any point of the run, also between tests.
/// </summary>
[Collection(MemoryBudget.FinalCollection)]
public sealed class MemoryBudgetFinalCheck
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void WholeRunPeak_StaysWithinBudget()
    {
        string? failure = MemoryBudget.WholeRunFailure();

        Assert.True(failure is null, failure);
    }
}
