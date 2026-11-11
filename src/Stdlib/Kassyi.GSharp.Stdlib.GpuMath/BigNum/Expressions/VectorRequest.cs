namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// What a vector node is asked to evaluate: elements <paramref name="offset"/> to <c>offset + count</c> of its capacity
/// (all the rest when <paramref name="count"/> is <see langword="null"/>), into <paramref name="target"/> when the
/// caller gave one. A flag node is always evaluated whole.
/// </summary>
/// <param name="offset">The first element.</param>
/// <param name="count">The number of elements, or <see langword="null"/> for the rest of the capacity.</param>
/// <param name="target">The caller's target, or the default storage.</param>
internal readonly struct VectorRequest(int offset, int? count, VectorStorage target)
{
    /// <summary>Gets the first element.</summary>
    public int Offset { get; } = offset;

    /// <summary>Gets the number of elements, or <see langword="null"/> for the rest of the capacity.</summary>
    public int? Count { get; } = count;

    /// <summary>Gets the caller's target, or the default storage.</summary>
    public VectorStorage Target { get; } = target;

    /// <summary>The whole vector, without a target.</summary>
    public static VectorRequest Whole => default;

    /// <summary>
    /// The window of this request over a node with <paramref name="capacity"/> elements, without the target: what an
    /// element-wise node asks of its inputs.
    /// </summary>
    public VectorRequest WindowOf(int capacity) => new(Offset, EvalCount(capacity), default);

    /// <summary>Gets the number of elements to evaluate of a node with <paramref name="capacity"/> elements.</summary>
    public int EvalCount(int capacity) => Count ?? (capacity - Offset);

    /// <summary>
    /// Checks the caller's integer target against the window of a node and borrows its first elements; the default
    /// view when there is no target.
    /// </summary>
    /// <exception cref="ArgumentException">The target has another width or fewer elements than the window.</exception>
    public GpuBigUIntArrayView IntTarget(int capacity, int wordsPerElement)
    {
        var target = Target.Words;
        return VectorResultCache.HasTarget(target)
            ? VectorResultCache.TargetValues(target, EvalCount(capacity), wordsPerElement)
            : default;
    }

    /// <summary>The same for a floating-point target.</summary>
    /// <exception cref="ArgumentException">The target has another width or fewer elements than the window.</exception>
    public GpuBigFloatArrayView FloatTarget(int capacity, int wordsPerElement)
    {
        var target = Target.FloatArray;
        return VectorResultCache.HasTarget(target)
            ? VectorResultCache.TargetFloat(target, EvalCount(capacity), wordsPerElement)
            : default;
    }
}
