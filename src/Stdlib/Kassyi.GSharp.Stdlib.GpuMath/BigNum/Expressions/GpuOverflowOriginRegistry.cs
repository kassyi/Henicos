namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// One checked operation launched on the GPU: the node, its operator and the evaluation that launched it. The text of
/// the expression is built only when an overflow is reported.
/// </summary>
internal sealed class OverflowSite(
    uint id,
    IGpuBigIntExpression node,
    string operation,
    int resultWords,
    EvaluationSite? evaluation,
    StackTrace? operatorStackTrace)
{
    public uint Id { get; } = id;

    /// <summary>Describes the operation for a <see cref="GpuOverflowException"/>.</summary>
    /// <returns>The public description of the origin.</returns>
    public GpuOverflowOrigin ToOrigin()
    {
        if (evaluation is null)
        {
            return new GpuOverflowOrigin
            {
                Operation = operation,
                ResultWords = resultWords,
                Subexpression = SignedExpressionText.Describe(node),
                OperatorStackTrace = operatorStackTrace?.ToString()
            };
        }

        var (subexpression, expression) = SignedExpressionText.Describe(evaluation.Root, node);
        return new GpuOverflowOrigin
        {
            Operation = operation,
            ResultWords = resultWords,
            Subexpression = subexpression,
            Expression = expression,
            EntryPoint = evaluation.EntryPoint,
            CallerMemberName = evaluation.CallerMemberName,
            CallerFilePath = evaluation.CallerFilePath,
            CallerLineNumber = evaluation.CallerLineNumber,
            OperatorStackTrace = operatorStackTrace?.ToString()
        };
    }
}

/// <summary>
/// The host side of the overflow origins of one workspace: a checked kernel receives the id of its operation, writes it
/// into the device status words when it overflows, and the read that reports the overflow finds the operation by the id.
/// </summary>
/// <remarks>
/// <para>
/// The ids of direct evaluations live in a ring of <see cref="Capacity"/> entries, so an overflow reported after that many
/// later checked operations has lost its origin (reported as unknown). The ids recorded in a captured CUDA graph are
/// replayed for the whole life of the graph, so the graph pins them (<see cref="Pin"/>) until it is destroyed.
/// </para>
/// <para>Thread-safe: ids come from an atomic counter, and the pinned table is locked.</para>
/// </remarks>
internal sealed class GpuOverflowOriginRegistry
{
    /// <summary>The number of ring entries (a power of two).</summary>
    public const int Capacity = 1024;

    private readonly OverflowSite?[] _ring = new OverflowSite?[Capacity];
    private readonly Dictionary<uint, OverflowSite> _pinned = [];
    private readonly Lock _lock = new();
    private int _next;

    /// <summary>Registers a checked operation that is about to be launched and returns its id (never 0).</summary>
    /// <param name="node">The node that launches the operation.</param>
    /// <param name="operation">The operator (<c>+</c>, <c>-</c>, <c>~</c>).</param>
    /// <param name="resultWords">The number of words of the result.</param>
    /// <param name="operatorStackTrace">The stack trace of the line that applied the operator, when captured.</param>
    /// <returns>The id passed to the kernel.</returns>
    public uint Register(IGpuBigIntExpression node, string operation, int resultWords, StackTrace? operatorStackTrace)
    {
        uint id;
        do
        {
            id = unchecked((uint)Interlocked.Increment(ref _next));
        }
        while (id == 0);

        var evaluation = EvaluationSite.Current;
        var site = new OverflowSite(id, node, operation, resultWords, evaluation, operatorStackTrace);
        Volatile.Write(ref _ring[id & (Capacity - 1)], site);
        evaluation?.RecordCaptured(site);
        return id;
    }

    /// <summary>Keeps the operations of a captured graph findable until <see cref="Unpin"/>.</summary>
    /// <param name="sites">The operations registered while the graph was captured.</param>
    public void Pin(IReadOnlyList<OverflowSite> sites)
    {
        if (sites.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var site in sites)
            {
                _pinned[site.Id] = site;
            }
        }
    }

    /// <summary>Releases the operations pinned by a graph that is destroyed.</summary>
    /// <param name="sites">The operations passed to <see cref="Pin"/>.</param>
    public void Unpin(IReadOnlyList<OverflowSite> sites)
    {
        if (sites.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var site in sites)
            {
                _pinned.Remove(site.Id);
            }
        }
    }

    /// <summary>Finds the operation of an id written by a checked kernel.</summary>
    /// <param name="id">The id, 0 when the kernel did not know it.</param>
    /// <returns>The origin, or <see cref="GpuOverflowOrigin.Unknown"/> when the id was overwritten.</returns>
    public GpuOverflowOrigin Find(uint id)
    {
        if (id == 0)
        {
            return GpuOverflowOrigin.Unknown;
        }

        lock (_lock)
        {
            if (_pinned.TryGetValue(id, out var pinned))
            {
                return pinned.ToOrigin();
            }
        }

        var site = Volatile.Read(ref _ring[id & (Capacity - 1)]);
        return site is not null && site.Id == id ? site.ToOrigin() : GpuOverflowOrigin.Unknown;
    }
}
