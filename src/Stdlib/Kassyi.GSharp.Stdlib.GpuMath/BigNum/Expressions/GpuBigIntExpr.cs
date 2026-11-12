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
/// <para>
/// <b>Result widths:</b> a result never overflows. Every node has an upper bound of its result in bits, computed
/// from its operands when the expression is built: <c>a + b</c> and <c>a - b</c> one bit more than the wider operand,
/// <c>a * b</c> the sum, <c>a / b</c> the dividend, <c>a % b</c> the narrower operand. The result has the words of
/// that bound (<see cref="IGpuBigIntExpression.NumWords"/>). Narrow it explicitly with <see cref="Fit"/>.
/// </para>
/// <para>
/// <b>Synchronization:</b> a sign that depends on the values (a difference of magnitudes, the operand Max or Min
/// chooses, <c>~</c> of a negative value) is written by a kernel on the device and read once, when the evaluation
/// returns; such an expression is captured into a CUDA graph, which reads the sign after each launch. Division and
/// remainder read the sizes of their operands and cannot be captured.
/// </para>
/// </remarks>
public readonly struct GpuBigIntExpr : IDisposable
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
#pragma warning disable GSH0024 // A leaf expression borrows its operand and allocates no disposable GPU resources.
    public static implicit operator GpuBigIntExpr(GpuBigInt b)
        => new(new GpuBigIntLeafExpression(b));
#pragma warning restore GSH0024

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
    /// Constructs a lazy bitwise NOT expression for the specified signed big integer expression: <c>~x == -x - 1</c>,
    /// as for <see cref="BigInteger"/> and <see cref="int"/>, independent of the word count.
    /// </summary>
    /// <param name="left">The signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the bitwise NOT AST node.</returns>
    /// <remarks>
    /// The result is one bit wider than the operand. For a negative operand a kernel tells a negative zero apart (the
    /// borrow of <c>|x| - 1</c>) and writes the sign on the device.
    /// </remarks>
    public static GpuBigIntExpr operator ~(GpuBigIntExpr left)
        => new(new SignedBitwiseNotExpression(left.Node));

    /// <summary>
    /// Constructs a lazy expression that resolves to the maximum of two signed big integer expressions.
    /// </summary>
    /// <param name="left">The first signed expression operand.</param>
    /// <param name="right">The second signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the Max AST node.</returns>
    /// <remarks>
    /// The result is one of the operands, its sign flag included. Of two equal values either may be returned, so the
    /// max of <c>+0</c> and <c>-0</c> may be <c>-0</c>, a zero with the minus flag, which equals 0.
    /// </remarks>
    public static GpuBigIntExpr Max(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedMaxExpression(left.Node, right.Node));

    /// <summary>
    /// Constructs a lazy expression that resolves to the minimum of two signed big integer expressions.
    /// </summary>
    /// <param name="left">The first signed expression operand.</param>
    /// <param name="right">The second signed expression operand.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> representing the Min AST node.</returns>
    /// <remarks>
    /// The result is one of the operands, its sign flag included. Of two equal values either may be returned, so the
    /// min of <c>+0</c> and <c>-0</c> may be <c>-0</c>, a zero with the minus flag, which equals 0.
    /// </remarks>
    public static GpuBigIntExpr Min(GpuBigIntExpr left, GpuBigIntExpr right)
        => new(new SignedMinExpression(left.Node, right.Node));

    /// <summary>
    /// Narrows this expression to <paramref name="words"/> words, checked: a magnitude that does not fit is a domain
    /// error, as a C# <c>checked</c> narrowing. The sign is kept.
    /// </summary>
    /// <param name="words">The word count of the result, at least 1.</param>
    /// <returns>A new <see cref="GpuBigIntExpr"/> whose result has <paramref name="words"/> words.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="words"/> is below 1.</exception>
    /// <remarks>
    /// <para>
    /// The result of an expression is as wide as its static bound (a sum one bit wider than its wider operand, a
    /// product as wide as both), so it never overflows. Use <c>Fit</c> to store it in a narrower buffer, for example
    /// the old width of an operand.
    /// </para>
    /// <para>
    /// <b>Domain error:</b> on the GPU it is reported as <see cref="OverflowException"/> at the next read of the
    /// workspace stream (or by <see cref="GpuWorkspace.ThrowIfDeviceErrors"/>); in CPU fallback mode it is thrown when
    /// the expression is evaluated.
    /// </para>
    /// <para>
    /// There is no unchecked counterpart (<c>Truncate</c>) for signed expressions. <see cref="GpuBigInt"/> stores a
    /// sign and a magnitude, so keeping the low words of the magnitude is not what a C# <c>unchecked</c> narrowing
    /// does: that keeps the low bits of the two's complement and reads them as a signed number of the narrower width.
    /// The two disagree whenever the kept magnitude reaches the sign bit: <c>unchecked((int)2147483648L)</c> is
    /// <c>int.MinValue</c>, while the sign and the low word of the magnitude stay <c>+2147483648</c>. Truncate the
    /// magnitude explicitly when that is what you mean.
    /// </para>
    /// </remarks>
    public GpuBigIntExpr Fit(int words)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(words, 1);
        return new GpuBigIntExpr(new SignedFitExpression(Node, words));
    }

    /// <summary>
    /// Evaluates this signed expression into the provided destination buffer.
    /// </summary>
    /// <param name="destination">The pre-allocated GPU buffer to store the evaluated magnitude, at least as wide as the
    /// result (<see cref="IGpuBigIntExpression.NumWords"/>); its words above the result are cleared. It may be the
    /// magnitude of an operand (<c>(a / b).Evaluate(a.Value)</c>): the root then computes into a temporary and copies
    /// the result.</param>
    /// <returns>A new <see cref="GpuBigInt"/> instance wrapping the destination buffer with the evaluated sign.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is narrower than the result; nothing is
    /// written. Narrow the expression with <see cref="Fit"/>.</exception>
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
    /// The expression, or any of its sub-expressions, is part of another CUDA graph that is currently alive, or
    /// the warm-up evaluation failed.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The expression divides or takes a remainder (<c>/</c>, <c>%</c>, and the StackLinq <c>Average</c>, which
    /// divides by the count): their sizes are read from the device, which a capture cannot record.
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

        // 1. Warm-up execution to trigger the lazy memory allocations and module loads before graph capture begins, so
        // that the capture finds its buffers in the pool instead of allocating them while it records.
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
        SignSource sign;
        try
        {
            result = EvaluateForCapture(workspace, targetResult, out sign);
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
            ReleaseAfterFailure(workspace, manager, manager.EndDeferral(deferral), result, owned, sign, graph,
                original);
            throw;
        }

        try
        {
            graphExec = workspace.Provider.InstantiateGraph(graph);
        }
        catch (Exception original)
        {
            ReleaseAfterFailure(workspace, manager, captured, result, owned, sign, graph, original);
            throw;
        }

        return new GpuBigIntExecutableGraph(workspace, graph, graphExec, result, owned, sign, captured, Node,
            ownsExpression);
    }

    // The evaluation inside the capture. A signed operator node keeps a sign that a kernel writes on the device: the
    // graph records that kernel and reads the word after each launch, instead of fixing the sign of the capture.
    private GpuBigInt EvaluateForCapture(GpuWorkspace workspace, GpuBigUInt? targetResult, out SignSource sign)
    {
        var value = Node is ISignedOperator root
            ? SignedTree.EvaluateSigned(root, workspace, targetResult)
            : SignedValue.FromHost(Node.Evaluate(workspace, targetResult));
        sign = value.Sign;
        return new GpuBigInt(value.Magnitude, value.Sign.HostNegative);
    }

    // A capture ended but no executable graph came of it: destroy the partial graph, return the deferred buffers and an
    // owned result, and give the expression back. As in AbortCapture, a failing step is thrown with the original (O14).
    private void ReleaseAfterFailure(GpuWorkspace workspace, DefaultGpuMemoryManager manager,
        CapturedResources captured, GpuBigInt result, bool ownsResult, SignSource sign, IntPtr graph,
        Exception original)
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

        if (sign.IsDevice)
        {
            RunCleanupStep((manager, sign.Word.Handle), static s => s.manager.Free(s.Handle), ref cleanupErrors);
        }

        GraphOwnership.Release(Node);
        ThrowIfCleanupFailed("Creating the CUDA graph failed, and so did its cleanup.", original, cleanupErrors);
    }

    // The failure path of a capture (O14): end the capture, drop the partial graph and give back the deferred buffers.
    // When that succeeds, the caller rethrows the original exception; when a step fails too, both are thrown together.
    internal static void AbortCapture(GpuWorkspace workspace, DefaultGpuMemoryManager manager, CaptureDeferral deferral,
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

    /// <summary>
    /// Releases any device buffers cached by the underlying expression nodes (such as vector reductions).
    /// </summary>
    /// <remarks>
    /// Idempotent across value-type copies and shared sub-expressions. Disposing an expression while a
    /// <see cref="GpuBigIntExecutableGraph"/> compiled from it is still alive throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The expression is currently owned by a live CUDA graph.</exception>
    public void Dispose()
    {
        if (Node is null)
        {
            return;
        }

        GraphOwnership.ThrowIfOwned(Node);
        (Node as IDisposable)?.Dispose();
    }
}
