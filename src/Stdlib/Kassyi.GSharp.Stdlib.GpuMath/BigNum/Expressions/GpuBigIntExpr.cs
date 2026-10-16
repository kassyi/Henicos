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
    public GpuBigInt Evaluate(GpuBigUInt destination) => Node.Evaluate(destination.Workspace, destination);

    /// <summary>
    /// Evaluates this signed expression into a newly rented temporary GPU buffer.
    /// </summary>
    /// <param name="workspace">The GPU workspace used for temporary buffer allocation.</param>
    /// <returns>A new <see cref="GpuBigInt"/> instance wrapping the temporary buffer with the evaluated sign.</returns>
    public GpuBigInt Evaluate(GpuWorkspace workspace) => Node.Evaluate(workspace);

    /// <summary>
    /// Compiles this expression tree into an executable CUDA Graph, eliminating launch overhead for repeated executions.
    /// The caller is responsible for disposing the returned graph.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="targetResult">An optional pre-allocated buffer for the final result.</param>
    /// <returns>An executable graph instance that can be launched repeatedly.</returns>
    public GpuBigIntExecutableGraph CompileToGraph(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        // 1. Warm-up execution to trigger all lazy memory allocations and JIT compilation
        // before graph capture begins, otherwise cuMemAlloc invalidates the stream.
        try
        {
            var warmupResult = Node.Evaluate(workspace, targetResult);
            if (!targetResult.HasValue)
            {
                // We don't need the warmup result if it's dynamically allocated
                warmupResult.Dispose();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed during warmup execution for CUDA Graph capture.", ex);
        }

        // 2. Capture the graph
        workspace.Provider.BeginCapture(workspace.Stream);

        GpuBigInt result;
        try
        {
            result = Node.Evaluate(workspace, targetResult);
        }
        catch
        {
            workspace.Provider.EndCapture(workspace.Stream);
            throw;
        }

        var graph = workspace.Provider.EndCapture(workspace.Stream);
        var graphExec = workspace.Provider.InstantiateGraph(graph);
        return new GpuBigIntExecutableGraph(workspace, graph, graphExec, result);
    }

    /// <summary>
    /// Evaluates this expression tree by dynamically compiling it into a CUDA Graph, launching it, and destroying it immediately.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="targetResult">An optional pre-allocated buffer for the final result.</param>
    /// <returns>A new <see cref="GpuBigInt"/> instance wrapping the evaluated result.</returns>
    public GpuBigInt EvaluateWithCudaGraph(GpuWorkspace workspace, GpuBigUInt? targetResult = null)
    {
        using var executableGraph = CompileToGraph(workspace, targetResult);
        executableGraph.Execute();
        return executableGraph.Result;
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
    /// <returns>A new <see cref="GpuBigInt"/> instance wrapping the evaluated result.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// <para>
    /// Only the final wait is asynchronous. Compilation (a warm-up evaluation, capture and instantiation) runs
    /// synchronously on the calling thread and includes host synchronization. To evaluate the same expression
    /// repeatedly, compile it once with <see cref="CompileToGraph"/> and call
    /// <see cref="GpuBigIntExecutableGraph.ExecuteAsync"/>.
    /// </para>
    /// <para>
    /// On cancellation, a result buffer allocated by this method is released in stream order behind the graph;
    /// <paramref name="targetResult"/> is left to the caller and receives the result once the graph completes.
    /// </para>
    /// </remarks>
    public async ValueTask<GpuBigInt> EvaluateWithCudaGraphAsync(GpuWorkspace workspace,
        GpuBigUInt? targetResult = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executableGraph = CompileToGraph(workspace, targetResult);
        try
        {
            await executableGraph.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return executableGraph.Result;
        }
        catch (OperationCanceledException) when (!targetResult.HasValue)
        {
            // Continuations run on pool threads, so the workspace context is activated explicitly.
            using (workspace.ActivateContext())
            {
                executableGraph.Result.Dispose();
            }

            throw;
        }
        finally
        {
            executableGraph.Dispose();
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
/// the launch completes. <see cref="Result"/> is not released by <see cref="Dispose"/>.
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
    /// Gets the buffer holding the result of the graph execution.
    /// </summary>
    /// <remarks>
    /// The contents are final once <see cref="Execute"/> returns or <see cref="ExecuteAsync"/> completes. After a
    /// canceled <see cref="ExecuteAsync"/> the graph is still running; reads ordered on the workspace stream (such as
    /// <see cref="GpuBigUInt.ToBigInteger"/>) wait for it and observe the completed result.
    /// </remarks>
    public GpuBigInt Result { get; }

    internal GpuBigIntExecutableGraph(GpuWorkspace workspace, IntPtr graph, IntPtr graphExec, GpuBigInt result)
    {
        _handles = new GraphHandles(workspace, graph, graphExec);
        Result = result;
    }

    /// <summary>
    /// Launches the executable graph on the GPU and waits for completion.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The graph has been disposed.</exception>
    public void Execute()
    {
        var handles = GetLiveHandles();
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
    /// <exception cref="ObjectDisposedException">The graph has been disposed.</exception>
    /// <remarks>Does not allocate in steady state.</remarks>
    public ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var handles = GetLiveHandles();
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        handles.Workspace.Provider.LaunchGraph(handles.GraphExec, handles.Workspace.Stream);
        return handles.Workspace.Provider.SynchronizeStreamAsync(handles.Workspace.Stream, cancellationToken);
    }

    /// <summary>
    /// Destroys the underlying CUDA Graph and executable graph instances.
    /// </summary>
    public void Dispose() => _handles?.Destroy();

    private GraphHandles GetLiveHandles() =>
        _handles is { IsDestroyed: false } handles
            ? handles
            : throw new ObjectDisposedException(nameof(GpuBigIntExecutableGraph));

    /// <summary>
    /// The graph handles shared by every copy of a <see cref="GpuBigIntExecutableGraph"/>.
    /// </summary>
    private sealed class GraphHandles(GpuWorkspace workspace, IntPtr graph, IntPtr graphExec)
    {
        private int _destroyed;

        public GpuWorkspace Workspace { get; } = workspace;

        public IntPtr GraphExec { get; } = graphExec;

        public bool IsDestroyed => Volatile.Read(ref _destroyed) != 0;

        public void Destroy()
        {
            if (Interlocked.Exchange(ref _destroyed, 1) != 0)
            {
                return;
            }

            // Dispose may run on a pool thread after an await, where the workspace context is not current.
            using (Workspace.ActivateContext())
            {
                Workspace.Provider.DestroyGraph(graph, GraphExec);
            }
        }
    }
}
