namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// The state of one capture that <c>CompileToGraph</c> runs on a workspace: the buffers freed during the capture, which
/// later allocations of the same capture may take again, and the handles the capture read (design 4.5.2, 4.5.3).
/// </summary>
/// <remarks>
/// The capture runs on one thread (the capture mode is THREAD_LOCAL), but a lock keeps the lists consistent if another
/// thread frees a buffer of the workspace meanwhile.
/// </remarks>
internal sealed class CaptureDeferral
{
    private readonly object _lock = new();
    private readonly Dictionary<int, Stack<GpuBigIntDeviceBuffer>> _free = [];
    private readonly HashSet<GpuBigIntDeviceBuffer> _leased = [];
    private readonly HashSet<(GpuBigIntDeviceBuffer Buffer, int Generation)> _seen = [];
    private readonly List<GpuBigIntMemoryHandle> _observed = [];

    // A buffer freed during the capture; the graph's nodes run in stream order, so a later allocation can reuse it
    public void AddFree(GpuBigIntDeviceBuffer buffer)
    {
        lock (_lock)
        {
            if (!_free.TryGetValue(buffer.TotalWords, out var stack))
            {
                stack = new Stack<GpuBigIntDeviceBuffer>();
                _free.Add(buffer.TotalWords, stack);
            }

            stack.Push(buffer);
        }
    }

    public GpuBigIntDeviceBuffer? TryTakeFree(int numWords)
    {
        lock (_lock)
        {
            return _free.TryGetValue(numWords, out var stack) && stack.TryPop(out var buffer) ? buffer : null;
        }
    }

    // A buffer leased during the capture belongs to the graph (an intermediate, the result or a deferred buffer)
    public void MarkLeased(GpuBigIntDeviceBuffer buffer)
    {
        lock (_lock)
        {
            _leased.Add(buffer);
        }
    }

    public void Observe(GpuBigIntMemoryHandle handle)
    {
        lock (_lock)
        {
            if (_seen.Add((handle.Buffer, handle.Generation)))
            {
                _observed.Add(handle);
            }
        }
    }

    public CapturedResources Complete()
    {
        lock (_lock)
        {
            var buffers = new List<GpuBigIntDeviceBuffer>();
            foreach (var stack in _free.Values)
            {
                buffers.AddRange(stack);
            }

            var foreign = new List<GpuBigIntMemoryHandle>();
            foreach (var handle in _observed)
            {
                if (!_leased.Contains(handle.Buffer))
                {
                    foreign.Add(handle);
                }
            }

            _free.Clear();
            return new CapturedResources(buffers.ToArray(), foreign.ToArray());
        }
    }
}

/// <summary>
/// What a finished capture hands to its graph.
/// </summary>
/// <param name="Buffers">The buffers freed during the capture and not taken again; the graph returns them.</param>
/// <param name="Operands">
/// The handles the capture read that the graph does not own (operands, target); the graph checks that each is still
/// alive before every launch.
/// </param>
internal readonly record struct CapturedResources(GpuBigIntDeviceBuffer[] Buffers, GpuBigIntMemoryHandle[] Operands);
