namespace Kassyi.GSharp.Core;

/// <summary>
/// Defines a composite scope that tracks and collectively manages the lifetime of GPU resources.
/// </summary>
public interface IGpuResourceScope : IDisposable
{
    /// <summary>
    /// Registers a disposable resource to be tracked by this scope.
    /// When the scope is disposed, all tracked resources will be disposed collectively.
    /// </summary>
    /// <param name="resource">The disposable resource to track.</param>
    void Track(IDisposable resource);

    /// <summary>
    /// Creates a nested child scope that is managed by the current scope.
    /// </summary>
    /// <returns>A new <see cref="IGpuResourceScope"/>.</returns>
    IGpuResourceScope CreateScope();
}
