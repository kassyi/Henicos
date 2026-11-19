using System.Runtime.ExceptionServices;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Records element-level exceptions on the current thread during <see cref="BoolVectorQuantifier"/> evaluation so
/// <c>Any</c> and <c>All</c> can compare the earliest throwing element index <c>T</c> against the earliest deciding
/// element index <c>D</c>.
/// </summary>
/// <remarks>
/// An element exception is kept when it has the lowest index so far, or the same index and an earlier record (the inner
/// node of an expression records first, as C# runs the inner projection of an element first).
/// </remarks>
internal sealed class PendingThrows
{
    [ThreadStatic]
    private static PendingThrows? t_current;

    private readonly PendingThrows? _previous;
    private int _nextOrder;
    private int _bestIndex = int.MaxValue;
    private int _bestOrder = int.MaxValue;

    private PendingThrows(PendingThrows? previous)
    {
        _previous = previous;
    }

    /// <summary>Gets whether a scope is active on the current thread.</summary>
    public static bool IsActive => t_current is not null;

    /// <summary>Gets the lowest element index recorded, or <see cref="int.MaxValue"/> before a record.</summary>
    public int MinIndex => _bestIndex;

    /// <summary>Gets the exception of the lowest element index, or <see langword="null"/>.</summary>
    public Exception? Exception { get; private set; }

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

    /// <summary>Throws the recorded exception with its original stack trace.</summary>
    /// <param name="exception">The recorded exception.</param>
    [DoesNotReturn]
    public static void ThrowRecorded(Exception exception) => ExceptionDispatchInfo.Throw(exception);

    private void Record(int index, Exception exception)
    {
        var order = _nextOrder++;
        if (Exception is null || index < _bestIndex || (index == _bestIndex && order < _bestOrder))
        {
            _bestIndex = index;
            _bestOrder = order;
            Exception = exception;
        }
    }

    /// <summary>A recording scope; disposing it restores the scope that was active before.</summary>
    /// <param name="instance">The records of the scope.</param>
    internal readonly ref struct Scope(PendingThrows instance)
    {
        /// <summary>Gets the records of the scope.</summary>
        public PendingThrows Instance => instance;

        /// <summary>Restores the scope that was active before.</summary>
        public void Dispose() => t_current = instance._previous;
    }

    /// <summary>A scope without recording; disposing it restores the scope that was active before.</summary>
    /// <param name="previous">The scope that was active before.</param>
    internal readonly ref struct SuspendScope(PendingThrows? previous)
    {
        /// <summary>Restores the scope that was active before.</summary>
        public void Dispose() => t_current = previous;
    }
}
