using System.Runtime.ExceptionServices;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Records element-level exceptions on the current thread during <see cref="VectorWalk"/> and
/// <see cref="BoolVectorQuantifier"/> evaluation so <c>Take</c>, <c>Count</c>, <c>Sum</c>, <c>Any</c>, and <c>All</c>
/// observe exceptions in C# evaluation order.
/// </summary>
/// <remarks>
/// <para>
/// An element exception is kept when it has the lowest index so far, or the same index and an earlier record (the inner
/// node of an expression records first, as C# runs the inner projection of an element first).
/// </para>
/// <para>
/// Index spaces can be nested with <see cref="OpenSpace"/> and <see cref="CloseSpace"/> so
/// <see cref="WhereDeviceState"/> can remap the winning operand failure through the predicate's exclusive prefix scan
/// into its <c>Skip</c> / <c>Take</c> window (clamping skipped elements to 0 and discarding failures past the window).
/// <see cref="RecordedOnInterrupt"/> returns the lowest-index record of the root space when all spaces are closed, or
/// the earliest-in-time live record of the open spaces when an inner space is still open.
/// </para>
/// </remarks>
internal sealed class PendingThrows
{
    private const int InitialOuterCapacity = 4;

    [ThreadStatic]
    private static PendingThrows? t_current;

    private readonly PendingThrows? _previous;
    private int _nextOrder;
    private Space _current = Space.Empty;
    private Space[]? _outer;
    private int _depth;

    private PendingThrows(PendingThrows? previous)
    {
        _previous = previous;
    }

    /// <summary>Gets whether a scope is active on the current thread.</summary>
    public static bool IsActive => t_current is not null;

    /// <summary>
    /// Gets the lowest element index recorded in the current space, or <see cref="int.MaxValue"/> before a record.
    /// </summary>
    public int MinIndex => _current.Best.Index;

    /// <summary>
    /// Gets the exception of the lowest element index in the current space, or <see langword="null"/>.
    /// </summary>
    public Exception? Exception => _current.Best.Exception;

    /// <summary>
    /// Gets the exception to throw when evaluation is interrupted: the lowest-index record of the root space when all
    /// spaces are closed, or the earliest-in-time record of the open spaces when an inner space is still open.
    /// </summary>
    public Exception? RecordedOnInterrupt => _depth == 0 ? _current.Best.Exception : FirstOpen();

    /// <summary>Starts recording on the current thread until the returned scope is disposed.</summary>
    /// <returns>The scope, which restores the previous one when disposed.</returns>
    public static Scope Begin()
    {
        var current = new PendingThrows(t_current);
        t_current = current;
        return new Scope(current);
    }

    /// <summary>Stops recording on the current thread until the returned scope is disposed.</summary>
    /// <returns>The scope, which restores the previous one when disposed.</returns>
    public static SuspendScope Suspend()
    {
        var previous = t_current;
        t_current = null;
        return new SuspendScope(previous);
    }

    /// <summary>
    /// Opens a nested index space when a scope is active on the current thread; does nothing otherwise.
    /// </summary>
    public static void OpenSpace() => t_current?.PushSpace();

    /// <summary>
    /// Closes the current index space and restores the enclosing space when a scope is active; returns
    /// <see cref="Recorded.Empty"/> otherwise.
    /// </summary>
    /// <returns>The winning record of the closed space, or <see cref="Recorded.Empty"/>.</returns>
    public static Recorded CloseSpace() => t_current is { } current ? current.PopSpace() : Recorded.Empty;

    /// <summary>
    /// Gets whether <paramref name="exception"/> is an error of an element value (a zero divisor, a value outside the
    /// domain of an operation), as opposed to an error of the operation itself (a disposed object, a precondition).
    /// </summary>
    /// <param name="exception">The exception thrown for an element.</param>
    /// <returns><see langword="true"/> for an error of the element value.</returns>
    public static bool IsElementException(Exception exception) =>
        exception is ArithmeticException || exception.GetType() == typeof(InvalidOperationException);

    /// <summary>Records <paramref name="exception"/> at <paramref name="index"/> when a scope is active.</summary>
    /// <param name="index">The element index, from the start of the evaluated window.</param>
    /// <param name="exception">The exception the element throws.</param>
    /// <returns>
    /// <see langword="true"/> when the exception is recorded and the caller continues; <see langword="false"/> when the
    /// caller throws it.
    /// </returns>
    public static bool TryRecord(int index, Exception exception)
    {
        if (t_current is not { } current)
        {
            return false;
        }

        current.Record(index, exception);
        return true;
    }

    /// <summary>
    /// Records <paramref name="exception"/> at <paramref name="index"/> when a scope is active, and throws it
    /// otherwise.
    /// </summary>
    /// <param name="index">The element index, from the start of the evaluated window.</param>
    /// <param name="exception">The exception the element throws.</param>
    /// <remarks>
    /// After a record the caller stops computing and sets its output from <paramref name="index"/> on to zero
    /// (<see cref="RecordedTail"/>), so that later host operations do not run on what the buffer held. Those values
    /// cannot change the answer of <c>Any</c> / <c>All</c> either way: the flags before the recorded index come from
    /// computed values, any deciding index at or after it throws the record, and an exception that a later node raises
    /// on them is replaced by the record.
    /// </remarks>
    public static void RecordOrThrow(int index, Exception exception)
    {
        if (!TryRecord(index, exception))
        {
            throw exception;
        }
    }

    /// <summary>Throws the recorded exception with its original stack trace.</summary>
    /// <param name="exception">The recorded exception.</param>
    [DoesNotReturn]
    public static void ThrowRecorded(Exception exception) => ExceptionDispatchInfo.Throw(exception);

    private Exception? FirstOpen()
    {
        if (_outer is { } outer)
        {
            for (var i = 0; i < _depth; i++)
            {
                if (outer[i].First is { } first)
                {
                    return first;
                }
            }
        }

        return _current.First;
    }

    private void Record(int index, Exception exception)
    {
        var first = _current.First ?? exception;
        var order = _nextOrder++;
        var best = _current.Best;
        if (best.Exception is null || index < best.Index || (index == best.Index && order < best.Order))
        {
            best = new Best(index, order, exception);
        }

        _current = new Space(best, first);
    }

    private void PushSpace()
    {
        if (_outer is null)
        {
            _outer = ArrayPool<Space>.Shared.Rent(InitialOuterCapacity);
        }
        else if (_depth == _outer.Length)
        {
            var grown = ArrayPool<Space>.Shared.Rent(_outer.Length * 2);
            Array.Copy(_outer, grown, _depth);
            ArrayPool<Space>.Shared.Return(_outer, clearArray: true);
            _outer = grown;
        }

        _outer[_depth++] = _current;
        _current = Space.Empty;
    }

    private Recorded PopSpace()
    {
        if (_depth == 0 || _outer is not { } outer)
        {
            return Recorded.Empty;
        }

        var closed = new Recorded(_current.Best.Index, _current.Best.Exception);
        _current = outer[--_depth];
        outer[_depth] = default;
        return closed;
    }

    private void ReleaseOuter()
    {
        if (_outer is not null)
        {
            ArrayPool<Space>.Shared.Return(_outer, clearArray: true);
            _outer = null;
            _depth = 0;
        }
    }

    /// <summary>A recording scope; disposing it restores the scope that was active before.</summary>
    /// <param name="instance">The records of the scope.</param>
    internal readonly ref struct Scope(PendingThrows instance)
    {
        /// <summary>Gets the records of the scope.</summary>
        public PendingThrows Instance => instance;

        /// <summary>Restores the scope that was active before and releases any pooled space stack.</summary>
        public void Dispose()
        {
            t_current = instance._previous;
            instance.ReleaseOuter();
        }
    }

    /// <summary>A scope without recording; disposing it restores the scope that was active before.</summary>
    /// <param name="previous">The scope that was active before.</param>
    internal readonly ref struct SuspendScope(PendingThrows? previous)
    {
        /// <summary>Restores the scope that was active before.</summary>
        public void Dispose() => t_current = previous;
    }

    /// <summary>The winning element exception of a closed index space.</summary>
    /// <param name="Index">
    /// The lowest recorded element index, or <see cref="int.MaxValue"/> when none was recorded.
    /// </param>
    /// <param name="Exception">The recorded exception, or <see langword="null"/> when none was recorded.</param>
    internal readonly record struct Recorded(int Index, Exception? Exception)
    {
        /// <summary>Gets an empty record with no exception and <see cref="int.MaxValue"/> index.</summary>
        public static Recorded Empty => new(int.MaxValue, null);
    }

    private readonly record struct Space(Best Best, Exception? First)
    {
        public static Space Empty => new(Best.Empty, null);
    }

    private readonly record struct Best(int Index, int Order, Exception? Exception)
    {
        public static Best Empty => new(int.MaxValue, int.MaxValue, null);
    }
}
