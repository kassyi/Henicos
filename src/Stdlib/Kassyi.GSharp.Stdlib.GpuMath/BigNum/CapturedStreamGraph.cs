using System.Runtime.ExceptionServices;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Captures a sequence of zero-sync stream operations into a CUDA Graph, replays it without host synchronization,
/// and returns its deferred buffers when disposed.
/// </summary>
internal sealed class CapturedStreamGraph : IDisposable
{
    private const uint NonBlockingStreamFlag = 1; // CU_STREAM_NON_BLOCKING

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
    /// Captures <paramref name="body"/> on a dedicated non-blocking CUDA stream, instantiates the graph, and returns a
    /// handle that replays it on the stream of <paramref name="workspace"/>. On failure, partial graph handles and
    /// deferred buffers are released first.
    /// </summary>
    /// <param name="workspace">The workspace whose memory manager hosts the capture and whose stream replays the graph.</param>
    /// <param name="body">The zero-sync operations to record into the graph.</param>
    /// <returns>A <see cref="CapturedStreamGraph"/> owning the instantiated graph and deferred buffers.</returns>
    public static CapturedStreamGraph Capture(GpuWorkspace workspace, Action body)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return Capture(workspace, body, workspace.Provider);
    }

    internal static CapturedStreamGraph Capture(GpuWorkspace workspace, Action body, IGpuGraphProvider graphProvider) =>
        TryCapture(workspace, body, graphProvider, out var graph)
            ? graph
            : throw new InvalidOperationException("A CUDA graph capture is already running on this workspace.");

    /// <summary>
    /// Tries to capture <paramref name="body"/> on a dedicated non-blocking CUDA stream and instantiate a graph that
    /// replays on the stream of <paramref name="workspace"/>. Returns <see langword="false"/> without throwing when
    /// another capture is already running on <paramref name="workspace"/>.
    /// </summary>
    /// <param name="workspace">The workspace whose memory manager hosts the capture and whose stream replays the graph.</param>
    /// <param name="body">The zero-sync operations to record into the graph.</param>
    /// <param name="graph">When this method returns <see langword="true"/>, the instantiated graph; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the graph was captured; <see langword="false"/> if another capture is already active.</returns>
    public static bool TryCapture(GpuWorkspace workspace, Action body, [NotNullWhen(true)] out CapturedStreamGraph? graph)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return TryCapture(workspace, body, workspace.Provider, out graph);
    }

    internal static bool TryCapture(GpuWorkspace workspace, Action body, IGpuGraphProvider graphProvider,
        [NotNullWhen(true)] out CapturedStreamGraph? graph)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(graphProvider);

        using (workspace.ActivateContext())
        {
            var manager = (DefaultGpuMemoryManager)workspace.MemoryManager;
            var captureStream = CreateCaptureStream(workspace);
            CapturedStreamGraph? result = null;
            Exception? original = null;
            try
            {
                if (!TryRecordStream(workspace, manager, graphProvider, captureStream, body, out var rawGraph,
                    out var captured))
                {
                    graph = null;
                    return false;
                }

                result = InstantiateCapturedGraph(workspace, manager, graphProvider, rawGraph, captured);
                graph = result;
                return true;
            }
            catch (Exception ex)
            {
                original = ex;
                throw;
            }
            finally
            {
                DestroyCaptureStream(captureStream, original, result);
            }
        }
    }

    private static IntPtr CreateCaptureStream(GpuWorkspace workspace)
    {
        if (workspace.BaseStream == IntPtr.Zero || GpuKernelLoader.IsCpuFallbackMode)
        {
            return IntPtr.Zero;
        }

        CudaException.ThrowIfError(CudaDriverApi.cuStreamCreate(out var captureStream, NonBlockingStreamFlag));
        return captureStream;
    }

    private static void DestroyCaptureStream(
        IntPtr captureStream, Exception? original, CapturedStreamGraph? result)
    {
        if (captureStream == IntPtr.Zero)
        {
            return;
        }

        List<Exception>? cleanupErrors = null;
        RunCleanupStep(captureStream, static stream =>
        {
            if (CudaDriverApi.cuStreamIsCapturing(stream, out var status) == CUresult.CudaSuccess &&
                status != CUstreamCaptureStatus.None)
            {
                CudaDriverApi.cuStreamEndCapture(stream, out var leakedGraph);
                if (leakedGraph != IntPtr.Zero)
                {
                    CudaDriverApi.cuGraphDestroy(leakedGraph);
                }
            }
        }, ref cleanupErrors);

        RunCleanupStep(captureStream,
            static stream => CudaException.ThrowIfError(CudaDriverApi.cuStreamDestroy(stream)),
            ref cleanupErrors);

        if (original is not null)
        {
            ThrowIfCleanupFailed("CUDA graph capture failed, and so did its cleanup.", original, cleanupErrors);
            return;
        }

        if (cleanupErrors is not null)
        {
            result?.Dispose();
            if (cleanupErrors.Count == 1)
            {
                ExceptionDispatchInfo.Throw(cleanupErrors[0]);
            }

            throw new AggregateException("Cleaning up the CUDA capture stream failed.", cleanupErrors);
        }
    }

    private static bool TryBeginRecording(
        DefaultGpuMemoryManager manager, IGpuGraphProvider graphProvider, IntPtr captureStream,
        [NotNullWhen(true)] out CaptureDeferral? deferral)
    {
        if (!manager.TryBeginDeferral(captureStream, out deferral))
        {
            return false;
        }

        try
        {
            graphProvider.BeginCapture(captureStream);
            return true;
        }
        catch
        {
            manager.EndDeferral(deferral);
            throw;
        }
    }

    private static bool TryRecordStream(
        GpuWorkspace workspace, DefaultGpuMemoryManager manager, IGpuGraphProvider graphProvider, IntPtr captureStream,
        Action body, out IntPtr graph, out CapturedResources captured)
    {
        if (!TryBeginRecording(manager, graphProvider, captureStream, out var deferral))
        {
            graph = IntPtr.Zero;
            captured = default;
            return false;
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

        graph = IntPtr.Zero;
        try
        {
            graph = graphProvider.EndCapture(captureStream);
            captured = manager.EndDeferral(deferral);
            return true;
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
                graph = graphProvider.EndCapture(deferral.Stream);
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
