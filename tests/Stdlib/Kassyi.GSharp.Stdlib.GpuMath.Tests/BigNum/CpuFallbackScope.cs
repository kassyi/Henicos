namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Forces the CPU fallback of <see cref="GpuKernelLoader"/> until disposed, then restores the previous value.
/// </summary>
/// <remarks>
/// <see cref="GpuKernelLoader.ForceCpuFallback"/> is AsyncLocal, so the override cannot leak into other tests.
/// Use it as <c>using var fallback = CpuFallbackScope.Enter();</c>.
/// </remarks>
internal readonly struct CpuFallbackScope : IDisposable
{
    private readonly bool _previous;

    private CpuFallbackScope(bool previous) => _previous = previous;

    /// <summary>Turns the CPU fallback on and returns the scope that turns it back.</summary>
    public static CpuFallbackScope Enter()
    {
        var scope = new CpuFallbackScope(GpuKernelLoader.ForceCpuFallback);
        GpuKernelLoader.ForceCpuFallback = true;
        return scope;
    }

    public void Dispose() => GpuKernelLoader.ForceCpuFallback = _previous;
}
