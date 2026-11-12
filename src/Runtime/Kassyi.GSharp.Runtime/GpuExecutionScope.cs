namespace Kassyi.GSharp.Runtime;

/// <summary>
/// The scope of a <see cref="GpuExecution.Use"/> selection: disposing it restores the selection that was in effect
/// before. A <see langword="default"/> scope, which <see cref="GpuExecution.Use"/> did not return, restores nothing.
/// </summary>
public readonly struct GpuExecutionScope : IDisposable
{
    private readonly KernelExecution _previous;
    private readonly bool _began;

    internal GpuExecutionScope(KernelExecution previous)
    {
        _previous = previous;
        _began = true;
    }

    /// <summary>Restores the selection that was in effect when the scope began.</summary>
    public void Dispose()
    {
        if (_began)
        {
            GpuExecution.Restore(_previous);
        }
    }
}
