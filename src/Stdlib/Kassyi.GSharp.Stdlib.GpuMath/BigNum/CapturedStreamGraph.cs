namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Captures a sequence of zero-sync stream operations into a CUDA Graph, replays it without host synchronization,
/// and returns its deferred buffers when disposed.
/// </summary>
internal sealed class CapturedStreamGraph : IDisposable
{
    private readonly DefaultGpuMemoryManager _manager;
    private readonly IGpuGraphProvider _graphProvider;
    private readonly IntPtr _graph;
    private readonly IntPtr _graphExec;
    private readonly CapturedResources _captured;
    private int _disposed;

    private CapturedStreamGraph(GpuWorkspace workspace, DefaultGpuMemoryManager manager,
        IGpuGraphProvider graphProvider, IntPtr graph, IntPtr graphExec, CapturedResources captured)
    {
        Workspace = workspace;
        _manager = manager;
        _graphProvider = graphProvider;
        _graph = graph;
        _graphExec = graphExec;
        _captured = captured;
    }

    internal GpuWorkspace Workspace { get; }

    internal GpuBigIntMemoryHandle[] Operands => _captured.Operands;

    /// <summary>
    /// Captures <paramref name="body"/> on the stream of <paramref name="workspace"/>, instantiates the graph, and
    /// returns the replayable handle. On failure, partial graph handles and deferred buffers are released first.
    /// </summary>
    /// <param name="workspace">The workspace whose stream and memory manager host the capture.</param>
    /// <param name="body">The zero-sync operations to record into the graph.</param>
    /// <returns>A <see cref="CapturedStreamGraph"/> owning the instantiated graph and deferred buffers.</returns>
    public static CapturedStreamGraph Capture(GpuWorkspace workspace, Action body)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return Capture(workspace, body, workspace.Provider);
    }

    internal static CapturedStreamGraph Capture(GpuWorkspace workspace, Action body, IGpuGraphProvider graphProvider)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(graphProvider);

        using (workspace.ActivateContext())
        {
            var manager = (DefaultGpuMemoryManager)workspace.MemoryManager;
            var (graph, captured) = RecordStream(workspace, manager, graphProvider, body);
            return InstantiateCapturedGraph(workspace, manager, graphProvider, graph, captured);
        }
    }

    private static (IntPtr Graph, CapturedResources Captured) RecordStream(
        GpuWorkspace workspace, DefaultGpuMemoryManager manager, IGpuGraphProvider graphProvider, Action body)
    {
        var deferral = manager.BeginDeferral();
        try
        {
            graphProvider.BeginCapture(workspace.Stream);
        }
        catch
        {
            manager.EndDeferral(deferral);
            throw;
        }

        try
        {
            body();
        }
        catch (Exception original)
        {
            AbortCapture(workspace, manager, graphProvider, deferral, original);
            throw;
        }

        var graph = IntPtr.Zero;
        try
        {
            graph = graphProvider.EndCapture(workspace.Stream);
            return (graph, manager.EndDeferral(deferral));
        }
        catch (Exception original)
        {
            ReleaseAfterFailure(manager, graphProvider, manager.EndDeferral(deferral), graph, original);
            throw;
        }
    }

    private static CapturedStreamGraph InstantiateCapturedGraph(
        GpuWorkspace workspace, DefaultGpuMemoryManager manager, IGpuGraphProvider graphProvider, IntPtr graph,
        CapturedResources captured)
    {
        try
        {
            var graphExec = graphProvider.InstantiateGraph(graph);
            return new CapturedStreamGraph(workspace, manager, graphProvider, graph, graphExec, captured);
        }
        catch (Exception original)
        {
            ReleaseAfterFailure(manager, graphProvider, captured, graph, original);
            throw;
        }
    }

    /// <summary>
    /// Launches the instantiated CUDA Graph on the workspace stream without synchronizing the host.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The graph has been disposed.</exception>
    public void Launch()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_graphExec == IntPtr.Zero || GpuKernelLoader.IsCpuFallbackMode)
        {
            return;
        }

        // Avoid boxing CudaContextScope via Workspace.ActivateContext() to keep steady-state launches allocation-free.
        if (Workspace.IsolatedContext.Handle != IntPtr.Zero)
        {
            using var scope = new GpuKernelLoader.CudaContextScope(Workspace.IsolatedContext.Handle);
            _graphProvider.LaunchGraph(_graphExec, Workspace.Stream);
            return;
        }

        _graphProvider.LaunchGraph(_graphExec, Workspace.Stream);
    }

    /// <summary>
    /// Destroys the CUDA Graph and executable graph handles and returns the deferred buffers to the memory pool.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Dispose may run on a pool thread where the workspace context is not current. Destroy the graph first; the
        // driver keeps a running launch alive, and the deferred buffer returns are ordered behind it.
        using (Workspace.ActivateContext())
        {
            try
            {
                _graphProvider.DestroyGraph(_graph, _graphExec);
            }
            finally
            {
                _manager.ReturnDeferred(_captured.Buffers);
            }
        }
    }

    // The failure path of a capture (O14): end the capture, drop the partial graph and give back the deferred buffers.
    // When that succeeds, the caller rethrows the original exception; when a step fails too, both are thrown together.
    internal static void AbortCapture(GpuWorkspace workspace, DefaultGpuMemoryManager manager, CaptureDeferral deferral,
        Exception original) =>
        AbortCapture(workspace, manager, workspace.Provider, deferral, original);

    private static void AbortCapture(GpuWorkspace workspace, DefaultGpuMemoryManager manager,
        IGpuGraphProvider graphProvider, CaptureDeferral deferral, Exception original)
    {
        using (workspace.ActivateContext())
        {
            List<Exception>? cleanupErrors = null;
            var graph = IntPtr.Zero;
            try
            {
                graph = graphProvider.EndCapture(workspace.Stream);
            }
            catch (Exception ex)
            {
                (cleanupErrors ??= []).Add(ex);
            }

            var captured = manager.EndDeferral(deferral);
            if (graph != IntPtr.Zero)
            {
                RunCleanupStep((graphProvider, graph), static s => s.graphProvider.DestroyGraph(s.graph, IntPtr.Zero),
                    ref cleanupErrors);
            }

            RunCleanupStep((manager, captured), static s => s.manager.ReturnDeferred(s.captured.Buffers),
                ref cleanupErrors);
            ThrowIfCleanupFailed("CUDA graph capture failed, and so did its cleanup.", original, cleanupErrors);
        }
    }

    // A capture ended but no executable graph came of it: destroy the partial graph and return the deferred buffers.
    // As in AbortCapture, a failing step is thrown with the original (O14).
    private static void ReleaseAfterFailure(DefaultGpuMemoryManager manager, IGpuGraphProvider graphProvider,
        CapturedResources captured, IntPtr graph, Exception original)
    {
        List<Exception>? cleanupErrors = null;
        if (graph != IntPtr.Zero)
        {
            RunCleanupStep((graphProvider, graph), static s => s.graphProvider.DestroyGraph(s.graph, IntPtr.Zero),
                ref cleanupErrors);
        }

        RunCleanupStep((manager, captured), static s => s.manager.ReturnDeferred(s.captured.Buffers),
            ref cleanupErrors);
        ThrowIfCleanupFailed("Creating the CUDA graph failed, and so did its cleanup.", original, cleanupErrors);
    }

    // Runs one cleanup step of a failure path; a step that throws is collected, so that the next steps still run.
    internal static void RunCleanupStep<TState>(TState state, Action<TState> step, ref List<Exception>? errors)
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
    // original), otherwise the original first (preserving any AggregateException wrapper intact) and then the cleanup
    // errors (O14).
    internal static void ThrowIfCleanupFailed(string message, Exception original, List<Exception>? errors)
    {
        if (errors is null)
        {
            return;
        }

        errors.Insert(0, original);
        throw new AggregateException(message, errors);
    }
}
