namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Combines two stack-allocated sequences element-wise with zero heap allocations.
/// </summary>
/// <typeparam name="TLeftEnumerator">The type of the first source enumerator.</typeparam>
/// <typeparam name="TRightEnumerator">The type of the second source enumerator.</typeparam>
/// <typeparam name="TLeft">The element type of the first sequence.</typeparam>
/// <typeparam name="TRight">The element type of the second sequence.</typeparam>
/// <typeparam name="TResult">The result element type.</typeparam>
public ref struct ZipStackEnumerator<TLeftEnumerator, TRightEnumerator, TLeft, TRight, TResult> : IStackEnumerator<TResult>
    where TLeftEnumerator : struct, IStackEnumerator<TLeft>, allows ref struct
    where TRightEnumerator : struct, IStackEnumerator<TRight>, allows ref struct
    where TResult : struct
{
    private TLeftEnumerator _left;
    private TRightEnumerator _right;
    private readonly Func<TLeft, TRight, TResult> _selector;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZipStackEnumerator{TLeftEnumerator, TRightEnumerator, TLeft, TRight, TResult}"/> struct.
    /// </summary>
    /// <param name="left">The first source enumerator.</param>
    /// <param name="right">The second source enumerator.</param>
    /// <param name="selector">A function that combines corresponding elements from both sequences.</param>
    public ZipStackEnumerator(TLeftEnumerator left, TRightEnumerator right, Func<TLeft, TRight, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _left = left;
        _right = right;
        _selector = selector;
    }

    /// <summary>
    /// Advances both enumerators and produces the combined element.
    /// </summary>
    /// <param name="current">When this method returns <see langword="true"/>, contains the combined element.</param>
    /// <returns><see langword="true"/> if both enumerators advanced; otherwise, <see langword="false"/>.</returns>
    public bool TryGetNext(out TResult current)
    {
        if (_left.TryGetNext(out var leftVal) && _right.TryGetNext(out var rightVal))
        {
            current = _selector(leftVal, rightVal);
            return true;
        }

        current = default;
        return false;
    }
}
