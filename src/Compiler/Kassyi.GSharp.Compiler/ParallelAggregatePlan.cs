namespace Kassyi.GSharp.Compiler;

/// <summary>The strategy of one aggregate, why it fell back to each thread, and whether its inputs are one in the grid.</summary>
internal readonly record struct ParallelAggregatePlan(
    ParallelAggregateStrategy Strategy,
    ParallelAggregateFallback Fallback,
    bool IsGridUniform);
