namespace Kassyi.GSharp.Runtime;

/// <summary>
/// The scope of a <see cref="GpuExecution.Use"/> selection: disposing it restores the selection that was in effect
/// before.
/// </summary>
public readonly struct GpuExecutionScope : IDisposable
{
    private readonly KernelExecution _previous;

    internal GpuExecutionScope(KernelExecution previous) => _previous = previous;

    /// <summary>Restores the selection that was in effect when the scope began.</summary>
    public void Dispose() => GpuExecution.Restore(_previous);
}
