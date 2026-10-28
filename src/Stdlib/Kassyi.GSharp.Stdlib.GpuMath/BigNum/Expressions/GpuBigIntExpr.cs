namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A lightweight value type wrapper that constructs and manages a lazy expression tree of signed GPU BigInteger arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Deferred sign resolution allows robust evaluation of operations where resultant signs depend on
/// runtime operand magnitudes (e.g. addition of different signs or subtraction of larger numbers).
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> As a readonly struct wrapping an immutable AST node, chaining operators creates lightweight expression trees without allocating device buffers until evaluated.
/// </para>
/// </remarks>
public readonly struct GpuBigIntExpr
{
    /// <summary>
    /// Gets the root AST node of this signed expression tree.
    /// </summary>
    public IGpuBigIntExpression Node { get; }

    internal GpuBigIntExpr(IGpuBigIntExpression node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
    }

    /// <summary>
    /// Implicitly wraps a <see cref="GpuBigInt"/> as a signed expression leaf node.
    /// </summary>
    /// <param name="b">The signed big integer instance.</param>
    public static implicit operator GpuBigIntExpr(GpuBigInt b)
        => new(new GpuBigIntLeafExpression(b));

    /// <summary>
    /// Constructs a lazy multiplication expression between two signed big integer expressions.
    /// </summary>
    /// <param name="left">The left signed expression operand.</param>
    /// <param name="right">The right signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the multiplication AST node.</returns>
    public static GpuBigIntExpr operator *(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedMulExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy division expression between two signed big integer expressions.
    /// </summary>
    /// <param name="left">The dividend signed expression operand.</param>
    /// <param name="right">The divisor signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the division AST node.</returns>
    public static GpuBigIntExpr operator /(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedDivExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy remainder (modulo) expression between two signed big integer expressions.
    /// </summary>
    /// <param name="left">The dividend signed expression operand.</param>
    /// <param name="right">The divisor signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the modulo AST node.</returns>
    public static GpuBigIntExpr operator %(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedModExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy addition expression with deferred sign resolution between two signed big integer expressions.
    /// </summary>
    /// <param name="left">The left signed expression operand.</param>
    /// <param name="right">The right signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the addition AST node.</returns>
    public static GpuBigIntExpr operator +(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedAddExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy subtraction expression with deferred sign resolution between two signed big integer expressions.
    /// </summary>
    /// <param name="left">The minuend signed expression operand.</param>
    /// <param name="right">The subtrahend signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the subtraction AST node.</returns>
    public static GpuBigIntExpr operator -(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedSubExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy negation expression for the specified signed big integer expression.
    /// </summary>
    /// <param name="left">The signed expression operand to negate.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the negation AST node.</returns>
    public static GpuBigIntExpr operator -(GpuBigIntExpr left)
        => new(new SignedNegateExpression(left.Node));

    /// <summary>
    /// Constructs a lazy bitwise NOT expression for the specified signed big integer expression.
    /// </summary>
    /// <param name="left">The signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the bitwise NOT AST node.</returns>
    public static GpuBigIntExpr operator ~(GpuBigIntExpr left)
        => new(new SignedBitwiseNotExpression(left.Node));

    /// <summary>
    /// Constructs a lazy expression that resolves to the maximum of two signed big integer expressions.
    /// </summary>
    /// <param name="left">The first signed expression operand.</param>
    /// <param name="right">The second signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the Max AST node.</returns>
    public static GpuBigIntExpr Max(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedMaxExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy expression that resolves to the minimum of two signed big integer expressions.
    /// </summary>
    /// <param name="left">The first signed expression operand.</param>
    /// <param name="right">The second signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the Min AST node.</returns>
    public static GpuBigIntExpr Min(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedMinExpression(left.Node, right.Node));

    /// <summary>
    /// Evaluates this signed expression into the provided destination buffer.
    /// </summary>
    /// <param name="destination">The pre-allocated GPU buffer to store the evaluated magnitude.</param>
    /// <returns>A new <see cref="GpuBigInt"/> instance wrapping the destination buffer with the evaluated sign.</returns>
    /// <exception cref="InvalidOperationException">The expression has been compiled into a CUDA graph that is alive.</exception>
    public GpuBigInt Evaluate(GpuBigUInt destination)
    {
        GraphOwnership.ThrowIfOwned(Node);
        return Node.Evaluate(destination.Workspace, destination);
    }

    /// <summary>
    /// Evaluates this signed expression into a newly rented temporary GPU buffer.
    /// </summary>
    /// <param name="workspace">The GPU workspace used for temporary buffer allocation.</param>
    /// <returns>A new <see cref="GpuBigInt"/> instance wrapping the temporary buffer with the evaluated sign.</returns>
    /// <exception cref="InvalidOperationException">The expression has been compiled into a CUDA graph that is alive.</exception>
    public GpuBigInt Evaluate(GpuWorkspace workspace)
    {
        GraphOwnership.ThrowIfOwned(Node);
        return Node.Evaluate(workspace);
    }

    /// <summary>
    /// Compiles this expression tree into an executable CUDA Graph, eliminating launch overhead for repeated executions.
    /// The caller is responsible for disposing the returned graph.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="targetResult">An optional pre-allocated buffer for the final result.</param>
    /// <returns>An executable graph instance that can be launched repeatedly.</returns>
    /// <remarks>
    /// The graph takes the expression: until the graph is disposed, evaluating or compiling this expression (or a copy
    /// of it) throws <see cref="InvalidOperationException"/>, because every launch of the graph reuses the buffers the
    /// expression's nodes hold. The graph also owns its result buffer (unless <paramref name="targetResult"/> is given)
    /// and the buffers its capture freed, and returns them when it is disposed. The operands and
    /// <paramref name="targetResult"/> stay with the caller; a launch after one of them was freed throws
    /// <see cref="ObjectDisposedException"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The expression has already been compiled into a graph that is alive, or the warm-up evaluation failed.
    /// </exception>
    public GpuBigIntExecutableGraph CompileToGraph(GpuWorkspace workspace, GpuBigUInt? targetResult = null) =>
        Compile(workspace, targetResult, ownsExpression: true);

    /// <summary>
    /// Evaluates this expression tree by dynamically compiling it into a CUDA Graph, launching it, and destroying it immediately.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="targetResult">An optional pre-allocated buffer for the final result.</param>
    /// <returns>
    /// The result. Without <paramref name="targetResult"/> the caller owns it and disposes it; with
    /// <paramref name="targetResult"/> it is a borrowed value of that buffer.
    /// </returns>
    /// <remarks>
    /// The expression stays usable afterwards: the internal graph holds it only while it runs (O15).
    /// </remarks>
    /// <exception cref="InvalidOperationException">The expression has been compiled into a CUDA graph that is alive.</exception>
    public GpuBigInt EvaluateWithCudaGraph(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        var executableGraph = Compile(workspace, targetResult, ownsExpression: false);
        try
        {
            executableGraph.Execute();
            return executableGraph.DetachResult();
        }
        finally
        {
            executableGraph.Dispose();
        }
    }

    /// <summary>
    /// Evaluates this expression tree through a CUDA Graph like <see cref="EvaluateWithCudaGraph"/>, but waits for the
    /// graph without blocking the calling thread.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="targetResult">An optional pre-allocated buffer for the final result.</param>
    /// <param name="cancellationToken">
    /// A token that stops waiting for the graph. The graph itself still runs to completion.
    /// </param>
    /// <returns>
    /// The result. Without <paramref name="targetResult"/> the caller owns it and disposes it; with
    /// <paramref name="targetResult"/> it is a borrowed value of that buffer.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// <para>
    /// Only the final wait is asynchronous. Compilation (a warm-up evaluation, capture and instantiation) runs
    /// synchronously on the calling thread and includes host synchronization. To evaluate the same expression
    /// repeatedly, compile it once with <see cref="CompileToGraph"/> and call
    /// <see cref="GpuBigIntExecutableGraph.ExecuteAsync"/>.
    /// </para>
    /// <para>
    /// While the internal graph runs, evaluating or compiling this expression throws
    /// <see cref="InvalidOperationException"/>; afterwards the expression is usable again (O15). On cancellation, a
    /// result buffer allocated by this method is released in stream order behind the graph; <paramref name="targetResult"/>
    /// is left to the caller and receives the result once the graph completes.
    /// </para>
    /// </remarks>
    public async ValueTask<GpuBigInt> EvaluateWithCudaGraphAsync(GpuWorkspace workspace,
        GpuBigUInt? targetResult = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executableGraph = Compile(workspace, targetResult, ownsExpression: false);
        try
        {
            await executableGraph.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return executableGraph.DetachResult();
        }
        finally
        {
            // Continuations run on pool threads; Dispose activates the workspace context itself. Without a detached
            // result it releases the result buffer in stream order behind the still running graph.
            executableGraph.Dispose();
        }
    }

    // Warm-up, capture and instantiation. ownsExpression: CompileToGraph hands the expression to the graph for its whole
    // life; EvaluateWithCudaGraph lends it only while its internal graph exists (O15).
    private GpuBigIntExecutableGraph Compile(GpuWorkspace workspace, GpuBigUInt? targetResult, bool ownsExpression)
    {
        GraphOwnership.ThrowIfOwned(Node);

        // 1. Warm-up execution to trigger all lazy memory allocations and JIT compilation
        // before graph capture begins, otherwise cuMemAlloc invalidates the stream.
        try
        {
            // A result allocated by the warm-up goes back to the pool, where the capture finds it again; a result in
            // the caller's target stays
            var warmupResult = Node.Evaluate(workspace, targetResult);
            if (!targetResult.HasValue)
            {
                warmupResult.Dispose();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed during warmup execution for CUDA Graph capture.", ex);
        }

        // 2. Capture the graph; the frees of the capture are deferred, so the graph keeps the buffers it uses. The
        // expression belongs to the graph from here on; every failure below gives it back.
        GraphOwnership.Take(Node);
        var manager = (DefaultGpuMemoryManager)workspace.MemoryManager;
        CaptureDeferral deferral;
        try
        {
            deferral = manager.BeginDeferral();
        }
        catch
        {
            GraphOwnership.Release(Node);
            throw;
        }

        try
        {
            workspace.Provider.BeginCapture(workspace.Stream);
        }
        catch
        {
            manager.EndDeferral(deferral);
            GraphOwnership.Release(Node);
            throw;
        }

        GpuBigInt result;
        try
        {
            result = Node.Evaluate(workspace, targetResult);
        }
        catch (Exception original)
        {
            try
            {
                AbortCapture(workspace, manager, deferral, original);
            }
            finally
            {
                GraphOwnership.Release(Node);
            }

            throw;
        }

        // The graph owns a result it allocated; a target stays with the caller, so the graph only borrows it
        var owned = !targetResult.HasValue && result.Value.OwnsBuffer;
        if (!owned)
        {
            result = new GpuBigInt(result.Value.AsBorrowed(), result.IsNegative);
        }

        var graph = IntPtr.Zero;
        var graphExec = IntPtr.Zero;
        CapturedResources captured;
        try
        {
            graph = workspace.Provider.EndCapture(workspace.Stream);
            captured = manager.EndDeferral(deferral);
        }
        catch (Exception original)
        {
            // The capture failed to end, or it ended and the deferral did not: give back what it took, including the
            // graph when EndCapture returned one
            ReleaseAfterFailure(workspace, manager, manager.EndDeferral(deferral), result, owned, graph, original);
            throw;
        }

        try
        {
            graphExec = workspace.Provider.InstantiateGraph(graph);
        }
        catch (Exception original)
        {
            ReleaseAfterFailure(workspace, manager, captured, result, owned, graph, original);
            throw;
        }

        return new GpuBigIntExecutableGraph(workspace, graph, graphExec, result, owned, captured, Node, ownsExpression);
    }

    // A capture ended but no executable graph came of it: destroy the partial graph, return the deferred buffers and an
    // owned result, and give the expression back. As in AbortCapture, a failing step is thrown with the original (O14).
    private void ReleaseAfterFailure(GpuWorkspace workspace, DefaultGpuMemoryManager manager,
        CapturedResources captured, GpuBigInt result, bool ownsResult, IntPtr graph, Exception original)
    {
        List<Exception>? cleanupErrors = null;
        if (graph != IntPtr.Zero)
        {
            RunCleanupStep((workspace, graph), static s => s.workspace.Provider.DestroyGraph(s.graph, IntPtr.Zero),
                ref cleanupErrors);
        }

        RunCleanupStep((manager, captured), static s => s.manager.ReturnDeferred(s.captured.Buffers), ref cleanupErrors);
        if (ownsResult)
        {
            RunCleanupStep((manager, result.Value.Handle), static s => s.manager.Free(s.Handle), ref cleanupErrors);
        }

        GraphOwnership.Release(Node);
        ThrowIfCleanupFailed("Creating the CUDA graph failed, and so did its cleanup.", original, cleanupErrors);
    }

    // The failure path of a capture (O14): end the capture, drop the partial graph and give back the deferred buffers.
    // When that succeeds, the caller rethrows the original exception; when a step fails too, both are thrown together.
    private static void AbortCapture(GpuWorkspace workspace, DefaultGpuMemoryManager manager, CaptureDeferral deferral,
        Exception original)
    {
        List<Exception>? cleanupErrors = null;
        var graph = IntPtr.Zero;
        try
        {
            graph = workspace.Provider.EndCapture(workspace.Stream);
        }
        catch (Exception ex)
        {
            (cleanupErrors ??= []).Add(ex);
        }

        var captured = manager.EndDeferral(deferral);
        if (graph != IntPtr.Zero)
        {
            RunCleanupStep((workspace, graph), static s => s.workspace.Provider.DestroyGraph(s.graph, IntPtr.Zero),
                ref cleanupErrors);
        }

        RunCleanupStep((manager, captured), static s => s.manager.ReturnDeferred(s.captured.Buffers), ref cleanupErrors);
        ThrowIfCleanupFailed("CUDA graph capture failed, and so did its cleanup.", original, cleanupErrors);
    }

    // Runs one cleanup step of a failure path; a step that throws is collected, so that the next steps still run
    private static void RunCleanupStep<TState>(TState state, Action<TState> step, ref List<Exception>? errors)
    {
        try
        {
            step(state);
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }
    }

    // After the cleanup of a failure path: nothing to throw when every step succeeded (the caller rethrows the
    // original), otherwise the original first and then the cleanup errors (O14)
    private static void ThrowIfCleanupFailed(string message, Exception original, List<Exception>? errors)
    {
        if (errors is null)
        {
            return;
        }

        errors.Insert(0, original);
        throw new AggregateException(message, errors);
    }
}

/// <summary>
/// Records which expression nodes a live CUDA graph owns (design 4.5.3-1).
/// </summary>
/// <remarks>
/// A graph replays the buffers its expression's nodes hold, so the expression must not be evaluated, compiled again or
/// changed while the graph is alive. The check sits in <see cref="GpuBigIntExpr"/>, through which every public
/// evaluation of a signed expression goes.
/// </remarks>
internal static class GraphOwnership
{
    private static readonly ConditionalWeakTable<IGpuBigIntExpression, object> s_owned = new();
    private static readonly object s_owner = new();

    public static void Take(IGpuBigIntExpression node)
    {
        if (!s_owned.TryAdd(node, s_owner))
        {
            throw new InvalidOperationException(
                "The expression was compiled into a CUDA graph; dispose the graph instead.");
        }
    }

    public static void Release(IGpuBigIntExpression node) => s_owned.Remove(node);

    public static void ThrowIfOwned(IGpuBigIntExpression node)
    {
        if (s_owned.TryGetValue(node, out _))
        {
            throw new InvalidOperationException(
                "The expression was compiled into a CUDA graph; dispose the graph instead.");
        }
    }
}

/// <summary>
/// Represents a compiled, executable CUDA Graph for a GpuBigInt expression tree.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime:</b> copies of this value share one set of graph handles. <see cref="Dispose"/> may be called on any copy
/// and more than once; afterwards every copy rejects execution with <see cref="ObjectDisposedException"/>.
/// Disposing while a launch is still running is safe and does not wait: the driver releases the executable graph once
/// the launch completes, and the buffers the graph owns go back to the pool in stream order behind it.
/// </para>
/// <para>
/// <b>Ownership:</b> the graph owns the expression it was compiled from, its result buffer (unless a target was given)
/// and the buffers its capture freed. <see cref="Result"/> is a view that is valid only while the graph is alive. The
/// operands and the target stay with the caller; <see cref="Execute"/> and <see cref="ExecuteAsync"/> throw
/// <see cref="ObjectDisposedException"/> instead of launching after one of them was freed.
/// </para>
/// <para>
/// <b>Thread safety:</b> launches are ordered on the workspace stream. Disposing concurrently with a launch on another
/// thread is not supported.
/// </para>
/// </remarks>
public readonly struct GpuBigIntExecutableGraph : IDisposable
{
    private readonly GraphHandles? _handles;

    /// <summary>
    /// Gets a view of the result of the graph execution, valid while the graph is alive.
    /// </summary>
    /// <remarks>
    /// The contents are final once <see cref="Execute"/> returns or <see cref="ExecuteAsync"/> completes. After a
    /// canceled <see cref="ExecuteAsync"/> the graph is still running; reads ordered on the workspace stream (such as
    /// <see cref="GpuBigUIntView.ToBigInteger"/>) wait for it and observe the completed result. After
    /// <see cref="Dispose"/> the result buffer is back in the pool, and reading the view throws
    /// <see cref="ObjectDisposedException"/>. To keep the value, pass a target to <see cref="GpuBigIntExpr.CompileToGraph"/>,
    /// copy it with <see cref="GpuBigIntView.ToOwned"/>, or use <see cref="GpuBigIntExpr.EvaluateWithCudaGraph"/>.
    /// </remarks>
    public GpuBigIntView Result => _handles is { } handles ? handles.Result : default;

    internal GpuBigIntExecutableGraph(GpuWorkspace workspace, IntPtr graph, IntPtr graphExec, GpuBigInt result,
        bool ownsResult, CapturedResources captured, IGpuBigIntExpression node, bool ownsExpression)
    {
        _handles = new GraphHandles(workspace, graph, graphExec, result, ownsResult, captured, node, ownsExpression);
    }

    /// <summary>
    /// Launches the executable graph on the GPU and waits for completion.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The graph, an operand or the target has been disposed.</exception>
    public void Execute()
    {
        var handles = GetLiveHandles();
        handles.ThrowIfOperandFreed();
        handles.Workspace.Provider.LaunchGraph(handles.GraphExec, handles.Workspace.Stream);
        handles.Workspace.Provider.SynchronizeStream(handles.Workspace.Stream);
    }

    /// <summary>
    /// Launches the executable graph on the GPU and waits for completion without blocking the calling thread.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token that stops waiting. An already canceled token prevents the launch; a later cancellation leaves the graph
    /// running to completion (see <see cref="Result"/>).
    /// </param>
    /// <returns>A <see cref="ValueTask"/> that completes when the graph has finished.</returns>
    /// <exception cref="ObjectDisposedException">The graph, an operand or the target has been disposed.</exception>
    /// <remarks>Does not allocate in steady state.</remarks>
    public ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var handles = GetLiveHandles();
        handles.ThrowIfOperandFreed();
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        handles.Workspace.Provider.LaunchGraph(handles.GraphExec, handles.Workspace.Stream);
        return handles.Workspace.Provider.SynchronizeStreamAsync(handles.Workspace.Stream, cancellationToken);
    }

    /// <summary>
    /// Destroys the underlying CUDA Graph and executable graph instances and returns the buffers the graph owns.
    /// </summary>
    public void Dispose() => _handles?.Destroy();

    // Hands the owned result to the caller once; Dispose then leaves it alone
    internal GpuBigInt DetachResult() => GetLiveHandles().DetachResult();

    private GraphHandles GetLiveHandles() =>
        _handles is { IsDestroyed: false } handles
            ? handles
            : throw new ObjectDisposedException(nameof(GpuBigIntExecutableGraph));

    /// <summary>
    /// The graph handles and the resources shared by every copy of a <see cref="GpuBigIntExecutableGraph"/>.
    /// </summary>
    private sealed class GraphHandles
    {
        private readonly IntPtr _graph;
        private readonly GpuBigInt _result;
        private readonly CapturedResources _captured;
        private readonly IGpuBigIntExpression _node;
        private readonly bool _ownsExpression;
        private int _ownsResult;
        private int _destroyed;

        public GraphHandles(GpuWorkspace workspace, IntPtr graph, IntPtr graphExec, GpuBigInt result, bool ownsResult,
            CapturedResources captured, IGpuBigIntExpression node, bool ownsExpression)
        {
            Workspace = workspace;
            _graph = graph;
            GraphExec = graphExec;
            _result = result;
            _ownsResult = ownsResult ? 1 : 0;
            _captured = captured;
            _node = node;
            _ownsExpression = ownsExpression;
        }

        public GpuWorkspace Workspace { get; }

        public IntPtr GraphExec { get; }

        public GpuBigIntView Result => _result;

        public bool IsDestroyed => Volatile.Read(ref _destroyed) != 0;

        public GpuBigInt DetachResult()
        {
            Interlocked.Exchange(ref _ownsResult, 0);
            return _result;
        }

        // The graph records raw pointers: a launch after an operand was freed would use memory of another owner
        public void ThrowIfOperandFreed()
        {
            foreach (var operand in _captured.Operands)
            {
                if (!operand.IsAlive)
                {
                    throw new ObjectDisposedException(nameof(GpuBigIntExecutableGraph),
                        $"An operand or the target of the graph ({operand}) has been disposed.");
                }
            }
        }

        public void Destroy()
        {
            if (Interlocked.Exchange(ref _destroyed, 1) != 0)
            {
                return;
            }

            // Dispose may run on a pool thread after an await, where the workspace context is not current. The graph is
            // destroyed first; the driver keeps a running launch alive, and the frees below are ordered behind it.
            using (Workspace.ActivateContext())
            {
                try
                {
                    Workspace.Provider.DestroyGraph(_graph, GraphExec);
                }
                finally
                {
                    var manager = (DefaultGpuMemoryManager)Workspace.MemoryManager;
                    if (Interlocked.Exchange(ref _ownsResult, 0) != 0)
                    {
                        manager.Free(_result.Value.Handle);
                    }

                    manager.ReturnDeferred(_captured.Buffers);
                    GraphOwnership.Release(_node);
                    if (_ownsExpression && _node is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }
    }
}
