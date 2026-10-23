namespace Kassyi.ZeroAlloc.Linq;

public static partial class StackLinqExtensions
{
    /// <summary>
    /// Returns elements from a stack-allocated sequence as long as a specified condition is true.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return elements from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence that occur before the element at which the test no longer passes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static StackEnumerable<TakeWhileEnumerator<TEnumerator, TSource>, TSource> TakeWhile<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<TakeWhileEnumerator<TEnumerator, TSource>, TSource>(
            new TakeWhileEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Returns elements from a stack-allocated sequence as long as a specified condition is true. The element's index is used in the logic of the predicate function.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return elements from.</param>
    /// <param name="predicate">A function to test each source element for a condition; the second parameter of the function represents the index of the source element.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence that occur before the element at which the test no longer passes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static StackEnumerable<TakeWhileAtEnumerator<TEnumerator, TSource>, TSource> TakeWhile<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, Func<TSource, int, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<TakeWhileAtEnumerator<TEnumerator, TSource>, TSource>(
            new TakeWhileAtEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Bypasses elements in a stack-allocated sequence as long as a specified condition is true and then returns the remaining elements.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return elements from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence starting at the first element that does not pass the test.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static StackEnumerable<SkipWhileEnumerator<TEnumerator, TSource>, TSource> SkipWhile<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, Func<TSource, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<SkipWhileEnumerator<TEnumerator, TSource>, TSource>(
            new SkipWhileEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Bypasses elements in a stack-allocated sequence as long as a specified condition is true and then returns the remaining elements. The element's index is used in the logic of the predicate function.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source sequence to return elements from.</param>
    /// <param name="predicate">A function to test each source element for a condition; the second parameter of the function represents the index of the source element.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements from the input sequence starting at the first element that does not pass the test.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static StackEnumerable<SkipWhileAtEnumerator<TEnumerator, TSource>, TSource> SkipWhile<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, Func<TSource, int, bool> predicate)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new StackEnumerable<SkipWhileAtEnumerator<TEnumerator, TSource>, TSource>(
            new SkipWhileAtEnumerator<TEnumerator, TSource>(source.Enumerator, predicate));
    }

    /// <summary>
    /// Returns a specified number of contiguous elements from the start of a stack-allocated sequence.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return elements from.</param>
    /// <param name="count">The number of elements to return.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains the specified number of elements from the start of the input sequence.</returns>
    public static StackEnumerable<TakeEnumerator<TEnumerator, TSource>, TSource> Take<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, int count)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        return new StackEnumerable<TakeEnumerator<TEnumerator, TSource>, TSource>(
            new TakeEnumerator<TEnumerator, TSource>(source.Enumerator, count));
    }

    /// <summary>
    /// Returns a new stack-allocated enumerable that contains the last <paramref name="count"/> elements from <paramref name="source"/>.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source span sequence to return elements from.</param>
    /// <param name="count">The number of elements to take from the end of the sequence.</param>
    /// <returns>A stack-allocated enumerable containing the last <paramref name="count"/> elements.</returns>
    public static StackEnumerable<SpanEnumerator<TSource>, TSource> TakeLast<TSource>(
        this StackEnumerable<SpanEnumerator<TSource>, TSource> source, int count)
    {
        var enumerator = source.Enumerator;
        var span = enumerator.GetRemainingSpanAndAdvance();
        int takeCount = Math.Min(count, span.Length);
        if (takeCount <= 0)
        {
            return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(ReadOnlySpan<TSource>.Empty));
        }
        return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(span.Slice(span.Length - takeCount, takeCount)));
    }

    /// <summary>
    /// Returns a new stack-allocated enumerable that contains the last <paramref name="count"/> elements from <paramref name="source"/>.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source array sequence to return elements from.</param>
    /// <param name="count">The number of elements to take from the end of the sequence.</param>
    /// <returns>A stack-allocated enumerable containing the last <paramref name="count"/> elements.</returns>
    public static StackEnumerable<SpanEnumerator<TSource>, TSource> TakeLast<TSource>(
        this StackEnumerable<ArrayEnumerator<TSource>, TSource> source, int count)
    {
        var enumerator = source.Enumerator;
        var span = enumerator.GetRemainingSpanAndAdvance();
        int takeCount = Math.Min(count, span.Length);
        if (takeCount <= 0)
        {
            return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(ReadOnlySpan<TSource>.Empty));
        }
        return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(span.Slice(span.Length - takeCount, takeCount)));
    }

    /// <summary>
    /// Bypasses a specified number of elements in a stack-allocated sequence and then returns the remaining elements.
    /// </summary>
    /// <typeparam name="TEnumerator">The type of the source enumerator.</typeparam>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The sequence to return elements from.</param>
    /// <param name="count">The number of elements to skip before returning the remaining elements.</param>
    /// <returns>A <see cref="StackEnumerable{TEnumerator, TSource}"/> that contains elements that occur after the specified index in the input sequence.</returns>
    public static StackEnumerable<SkipEnumerator<TEnumerator, TSource>, TSource> Skip<TEnumerator, TSource>(
        this StackEnumerable<TEnumerator, TSource> source, int count)
        where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct
    {
        return new StackEnumerable<SkipEnumerator<TEnumerator, TSource>, TSource>(
            new SkipEnumerator<TEnumerator, TSource>(source.Enumerator, count));
    }

    /// <summary>
    /// Returns a new stack-allocated enumerable that contains the elements from <paramref name="source"/> with the last <paramref name="count"/> elements omitted.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source span sequence to omit elements from.</param>
    /// <param name="count">The number of elements to omit from the end of the sequence.</param>
    /// <returns>A stack-allocated enumerable containing elements with the last <paramref name="count"/> elements omitted.</returns>
    public static StackEnumerable<SpanEnumerator<TSource>, TSource> SkipLast<TSource>(
        this StackEnumerable<SpanEnumerator<TSource>, TSource> source, int count)
    {
        var enumerator = source.Enumerator;
        var span = enumerator.GetRemainingSpanAndAdvance();
        int skipCount = Math.Clamp(count, 0, span.Length);
        if (span.Length - skipCount <= 0)
        {
            return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(ReadOnlySpan<TSource>.Empty));
        }
        return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(span.Slice(0, span.Length - skipCount)));
    }

    /// <summary>
    /// Returns a new stack-allocated enumerable that contains the elements from <paramref name="source"/> with the last <paramref name="count"/> elements omitted.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the sequence.</typeparam>
    /// <param name="source">The source array sequence to omit elements from.</param>
    /// <param name="count">The number of elements to omit from the end of the sequence.</param>
    /// <returns>A stack-allocated enumerable containing elements with the last <paramref name="count"/> elements omitted.</returns>
    public static StackEnumerable<SpanEnumerator<TSource>, TSource> SkipLast<TSource>(
        this StackEnumerable<ArrayEnumerator<TSource>, TSource> source, int count)
    {
        var enumerator = source.Enumerator;
        var span = enumerator.GetRemainingSpanAndAdvance();
        int skipCount = Math.Clamp(count, 0, span.Length);
        if (span.Length - skipCount <= 0)
        {
            return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(ReadOnlySpan<TSource>.Empty));
        }
        return new StackEnumerable<SpanEnumerator<TSource>, TSource>(new SpanEnumerator<TSource>(span.Slice(0, span.Length - skipCount)));
    }
}
