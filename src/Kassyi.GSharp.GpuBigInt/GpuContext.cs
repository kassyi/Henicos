namespace Kassyi.GSharp.GpuBigInt;

public class GpuContext : IDisposable
{
    public int MaxWords { get; }
    private bool _disposed;

    // Memory pooling for Lazy Evaluation
    private readonly Dictionary<int, Stack<GpuBigInteger>> _tempPool = new();

    public GpuContext(int maxWords)
    {
        MaxWords = maxWords;
        if (!NativeMethods.GpuBigInt_Init(maxWords))
        {
            throw new InvalidOperationException("Failed to initialize GPU context.");
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

            NativeMethods.GpuBigInt_Cleanup();
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
