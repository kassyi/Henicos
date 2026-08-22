namespace Kassyi.GSharp.GpuBigInt;

public enum GpuExecutionEngine
{
    LegacyNative,
    GSharpJit
}

public class GpuContext : IDisposable
{
    public int MaxWords { get; }
    public IntPtr WorkspacePointer { get; }
    public IntPtr CarryBuffer { get; }
    public GpuExecutionEngine Engine { get; set; }
    private bool _disposed;

    // Memory pooling for Lazy Evaluation
    private readonly Dictionary<int, Stack<GpuBigInteger>> _tempPool = new();

    public GpuContext(int maxWords, GpuExecutionEngine engine = GpuExecutionEngine.GSharpJit)
    {
        MaxWords = maxWords;
        Engine = engine;
        if (Engine == GpuExecutionEngine.GSharpJit)
        {
            // Allocate a carry buffer large enough for MaxWords
            var chunkSize = 4;
            var numChunks = (maxWords + chunkSize - 1) / chunkSize;
            Core.CudaException.ThrowIfError(
                Core.CudaDriverApi.cuMemAlloc_v2(out var carryBuf, (numChunks + 1) * 8));
            CarryBuffer = carryBuf;
        }
        else
        {
            WorkspacePointer = NativeMethods.GpuBigInt_Init(maxWords);
            if (WorkspacePointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to initialize GPU context.");
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Dispose all pooled temporary buffers
            foreach (var stack in _tempPool.Values)
            {
                while (stack.Count > 0)
                {
                    var temp = stack.Pop();
                    // Avoid circular calls since temp.Dispose() calls back into context or disposes native
                    // Normally GpuBigInteger.Dispose calls NativeMethods.GpuBigInt_Free
                    temp.DisposeCore();
                }
            }

            _tempPool.Clear();

            if (Engine == GpuExecutionEngine.GSharpJit)
            {
                if (CarryBuffer != IntPtr.Zero)
                {
                    Core.CudaDriverApi.cuMemFree_v2(CarryBuffer);
                }
            }
            else
            {
                if (WorkspacePointer != IntPtr.Zero)
                {
                    NativeMethods.GpuBigInt_Cleanup(WorkspacePointer);
                }
            }

            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    ~GpuContext()
    {
        Dispose();
    }

    internal GpuBigInteger RentTemp(int numWords)
    {
        if (_tempPool.TryGetValue(numWords, out var stack) && stack.Count > 0)
        {
            var temp = stack.Pop();
            temp.IsTemp = true; // ensure it's marked as temp again if needed
            return temp;
        }

        // Allocate a new one if pool is empty
        var newTemp = new GpuBigInteger(this, numWords);
        newTemp.IsTemp = true;
        return newTemp;
    }

    internal void ReturnTemp(GpuBigInteger temp)
    {
        if (!temp.IsTemp)
        {
            return;
        }

        if (!_tempPool.TryGetValue(temp.NumWords, out var stack))
        {
            stack = new Stack<GpuBigInteger>();
            _tempPool[temp.NumWords] = stack;
        }

        stack.Push(temp);
    }
}
