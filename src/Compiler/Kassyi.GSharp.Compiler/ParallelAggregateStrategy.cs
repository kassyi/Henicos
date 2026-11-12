namespace Kassyi.GSharp.Compiler;

/// <summary>How the CUDA dialect evaluates one <c>AsParallel()</c> aggregate. The CPU dialects run it in each thread.</summary>
internal enum ParallelAggregateStrategy
{
    /// <summary>Each thread sums the whole sequence on its own.</summary>
    PerThread,

    /// <summary>The threads of a block share the elements and combine their partial sums (every block sums all of it).</summary>
    Block,

    /// <summary>The threads of the grid share the elements and combine through a grid sync (cooperative kernels).</summary>
    Grid
}
