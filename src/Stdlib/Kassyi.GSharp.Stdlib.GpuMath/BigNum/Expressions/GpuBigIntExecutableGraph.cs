namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

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
        bool ownsResult, SignSource sign, CapturedResources captured, IGpuBigIntExpression node,
        bool ownsExpression)
    {
        _handles = new GraphHandles(workspace, graph, graphExec, result, ownsResult, sign, captured, node,
            ownsExpression);
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
        handles.ReadSign();
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
    /// <remarks>
    /// Does not allocate in steady state, unless the result's sign is decided on the device: it is read in a
    /// continuation after the wait. Disposing the graph while the task is pending is safe; the task completes and
    /// the sign is not read.
    /// </remarks>
    public ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var handles = GetLiveHandles();
        handles.ThrowIfOperandFreed();
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        handles.Workspace.Provider.LaunchGraph(handles.GraphExec, handles.Workspace.Stream);
        return handles.HasDeviceSign
            ? CompleteAsync(handles, cancellationToken)
            : handles.Workspace.Provider.SynchronizeStreamAsync(handles.Workspace.Stream, cancellationToken);
    }

    // The wait for the launch, then the read of a device sign (one word, after the stream is idle)
    private static async ValueTask CompleteAsync(GraphHandles handles, CancellationToken cancellationToken)
    {
        await handles.Workspace.Provider.SynchronizeStreamAsync(handles.Workspace.Stream, cancellationToken)
            .ConfigureAwait(false);

        // Continuations run on pool threads, where the workspace context is not current
        using (handles.Workspace.ActivateContext())
        {
            handles.ReadSign();
        }
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
        // The device word of a sign the graph writes, or the default value (a host sign)
        private readonly GpuBigIntMemoryHandle _signWord;
        private readonly CapturedResources _captured;
        private int _negative;
        private readonly IGpuBigIntExpression _node;
        private readonly bool _ownsExpression;
        private int _ownsResult;
        private int _destroyed;

        public GraphHandles(GpuWorkspace workspace, IntPtr graph, IntPtr graphExec, GpuBigInt result, bool ownsResult,
            SignSource sign, CapturedResources captured, IGpuBigIntExpression node,
            bool ownsExpression)
        {
            _signWord = sign.IsDevice ? sign.Word.Handle : default;
            _negative = result.IsNegative ? 1 : 0;
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

        // The magnitude with the sign of the last launch
        public GpuBigIntView Result => new(_result.Value, Volatile.Read(ref _negative) != 0);

        // After a launch: a sign the graph writes on the device is read (one word); a host sign does not change
        // A graph disposed while ExecuteAsync waited has retired its word: the continuation keeps the previous sign
        // (Result cannot be read after Dispose anyway) instead of faulting the awaited task.
        public void ReadSign()
        {
            if (_signWord.IsDefault || IsDestroyed)
            {
                return;
            }

            try
            {
                var word = new GpuBigUInt(Workspace, _signWord.Borrow());
                Volatile.Write(ref _negative, SignedDeviceOps.ReadNegative(Workspace, SignSource.Device(word)) ? 1 : 0);
            }
            catch (ObjectDisposedException) when (IsDestroyed)
            {
                // Destroy ran between the check above and the read: the word is gone, and so is the graph whose
                // Result would show the sign; the previous sign stays
                return;
            }
        }

        public bool IsDestroyed => Volatile.Read(ref _destroyed) != 0;

        // Whether the result's sign is a device word the graph writes
        public bool HasDeviceSign => !_signWord.IsDefault;

        public GpuBigInt DetachResult()
        {
            Interlocked.Exchange(ref _ownsResult, 0);
            return new GpuBigInt(_result.Value, Volatile.Read(ref _negative) != 0);
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

                    if (!_signWord.IsDefault)
                    {
                        manager.Free(_signWord);
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
