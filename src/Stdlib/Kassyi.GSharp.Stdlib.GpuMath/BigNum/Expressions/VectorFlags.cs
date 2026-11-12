namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The result buffer of a flag node (one word per element: 1 = true, 0 = false), allocated at the first evaluation with
/// the node's capacity and reused by every later one.
/// </summary>
internal sealed class VectorFlags : IDisposable
{
    private GpuBigUIntArray _values;
    private bool _allocated;

    /// <summary>Lends the flags, allocating them on the first call.</summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="capacity">The capacity of the node.</param>
    /// <returns>The flags.</returns>
    public GpuBigUIntArray Get(GpuWorkspace workspace, int capacity)
    {
        if (!_allocated)
        {
            _values = new GpuBigUIntArray(workspace, capacity, 1);
            _allocated = true;
        }

        return _values;
    }

    /// <summary>Returns the flags; a second call does nothing.</summary>
    public void Dispose()
    {
        if (_allocated)
        {
            _values.Dispose();
            _allocated = false;
        }
    }
}
