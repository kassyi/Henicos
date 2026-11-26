#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests;

/// <summary>
/// Owns a <see cref="GpuKernelLoader.CudaContextScope"/> and a <see cref="GpuWorkspace"/> for test fixtures,
/// disposing the scope if workspace creation throws and disposing both in reverse order.
/// </summary>
public sealed class GpuTestWorkspace : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _contextScope;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuTestWorkspace"/> class.
    /// </summary>
    /// <param name="maxWords">The maximum word capacity configured on the workspace.</param>
    /// <param name="engine">The GPU execution engine to use.</param>
    /// <param name="useIsolatedContext">Whether to create an isolated CUDA context for the workspace.</param>
    public GpuTestWorkspace(
        int maxWords,
        GpuExecutionEngine engine = GpuExecutionEngine.GSharpAot,
        bool useIsolatedContext = false)
        : this(() => new GpuWorkspace(maxWords, engine, useIsolatedContext))
    {
    }

    internal GpuTestWorkspace(Func<GpuWorkspace> createWorkspace)
    {
        ArgumentNullException.ThrowIfNull(createWorkspace);
        _contextScope = new GpuKernelLoader.CudaContextScope();
        try
        {
            Workspace = createWorkspace();
        }
        catch
        {
            _contextScope.Dispose();
            throw;
        }
    }

    /// <summary>Gets the owned <see cref="GpuWorkspace"/>.</summary>
    public GpuWorkspace Workspace { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            Workspace.Dispose();
        }
        finally
        {
            _contextScope.Dispose();
        }
    }
}
