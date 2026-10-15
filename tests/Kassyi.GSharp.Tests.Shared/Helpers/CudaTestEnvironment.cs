namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>
/// Device availability checks and context scoping shared by tests that drive the CUDA driver directly.
/// </summary>
internal static class CudaTestEnvironment
{
    /// <summary>
    /// Skips the current test when no CUDA device is available.
    /// </summary>
    /// <remarks>
    /// <see cref="GpuKernelLoader.IsCpuFallbackMode"/> is only populated by driver initialization, so initialization
    /// runs first. Without it, a host without a device would report <see langword="false"/> and not skip.
    /// </remarks>
    public static void SkipIfNoDevice()
    {
        GpuKernelLoader.EnsureInitialized();
        Skip.If(GpuKernelLoader.IsCpuFallbackMode, "Requires a CUDA device.");
    }

    /// <summary>
    /// Runs <paramref name="action"/> with the CUDA context current on the calling thread and returns its result.
    /// </summary>
    /// <remarks>
    /// <see cref="GpuKernelLoader.CudaContextScope"/> binds the context to the calling OS thread, so async tests use this
    /// for their synchronous sections instead of holding a scope across an <c>await</c>.
    /// </remarks>
    public static T InContext<T>(Func<T> action)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        return action();
    }

    /// <summary>
    /// Runs <paramref name="action"/> with the CUDA context current on the calling thread.
    /// </summary>
    public static void InContext(Action action)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        action();
    }
}
