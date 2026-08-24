using Kassyi.GSharp.Stdlib.Math.Interop;

namespace Kassyi.GSharp.Stdlib.Math;

public enum GpuExecutionEngine
{
    LegacyNative,
    GSharpJit
}

public class GpuContext : IDisposable
{
    public int MaxWords { get; }
    public IntPtr WorkspacePointer { get; }
    public GpuExecutionEngine Engine { get; set; }

    public Providers.IGpuExecutionProvider Provider => Engine == GpuExecutionEngine.GSharpJit
        ? Providers.GSharpJitProvider.Instance
        : Providers.NativeProvider.Instance;

    private bool _disposed;

    // Memory pooling for Lazy Evaluation
    private readonly
        System.Collections.Concurrent.ConcurrentDictionary<int,
            System.Collections.Concurrent.ConcurrentBag<GpuBigInteger>> _tempPool = new();

    public GpuContext(int maxWords, GpuExecutionEngine engine = GpuExecutionEngine.GSharpJit)
    {
        MaxWords = maxWords;
        Engine = engine;
        if (Engine == GpuExecutionEngine.GSharpJit)
        {
            // CarryBuffer allocation logic removed for thread safety; now handled dynamically by IGpuExecutionProvider.
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
            foreach (var bag in _tempPool.Values)
            {
                while (bag.TryTake(out var temp))
                {
                    // Avoid circular calls since temp.Dispose() calls back into context or disposes native
                    // Normally GpuBigInteger.Dispose calls NativeMethods.GpuBigInt_Free
                    temp.DisposeCore();
                }
            }

            _tempPool.Clear();

            if (Engine == GpuExecutionEngine.GSharpJit)
            {
                // JIT-specific cleanup (if any)
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
        if (_tempPool.TryGetValue(numWords, out var bag) && bag.TryTake(out var temp))
        {
            temp.IsTemp = true; // ensure it's marked as temp again if needed
            return temp;
        }

        // Allocate a new one if pool is empty
        var newTemp = new GpuBigInteger(this, numWords)
        {
            IsTemp = true
        };
        return newTemp;
    }

    internal void ReturnTemp(GpuBigInteger temp)
    {
        if (!temp.IsTemp)
        {
            return;
        }

        var bag = _tempPool.GetOrAdd(temp.NumWords, _ => []);
        bag.Add(temp);
    }
}

