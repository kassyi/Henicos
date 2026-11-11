namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Where a node is in the recursion of <see cref="VectorWalk.EvaluateDirect"/>: its depth, the number of levels
/// evaluated by recursion, and what happens below them (the walk takes over, or the evaluation is refused).
/// </summary>
internal readonly struct RecursionLevel
{
    private RecursionLevel(int depth, int limit, bool walksBelow)
    {
        Depth = depth;
        Limit = limit;
        WalksBelow = walksBelow;
    }

    /// <summary>Gets the number of vector nodes above this one.</summary>
    public int Depth { get; }

    /// <summary>Gets the number of levels evaluated by recursion.</summary>
    public int Limit { get; }

    /// <summary>
    /// Gets whether the levels below <see cref="Limit"/> are walked (<c>Evaluate</c>) instead of refused
    /// (<c>EvaluateOnCallStack</c>).
    /// </summary>
    public bool WalksBelow { get; }

    /// <summary>Gets whether a node at this level is evaluated by recursion.</summary>
    public bool Recurses => Depth < Limit;

    /// <summary>Gets the level of the inputs of a node at this level.</summary>
    public RecursionLevel Next => new(Depth + 1, Limit, WalksBelow);

    /// <summary>
    /// The root of an evaluation that recurses through <paramref name="limit"/> levels and walks below.
    /// </summary>
    public static RecursionLevel Walking(int limit) => new(0, limit, true);

    /// <summary>The root of an evaluation that recurses through at most <paramref name="limit"/> levels.</summary>
    public static RecursionLevel Bounded(int limit) => new(0, limit, false);
}
