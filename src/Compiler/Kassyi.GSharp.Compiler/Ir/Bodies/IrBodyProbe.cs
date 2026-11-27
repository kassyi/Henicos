namespace Kassyi.GSharp.Compiler.Ir.Bodies;

/// <summary>
/// The measuring points of the IR body pipeline: the stages report their time to an observer a benchmark sets.
/// </summary>
internal static class IrBodyProbe
{
    private static readonly AsyncLocal<IrBodyObserver?> s_current = new();

    /// <summary>The observer of this flow of execution; <see langword="null"/> unless a benchmark set one.</summary>
    public static IrBodyObserver? Current => s_current.Value;

    /// <summary>
    /// Makes <paramref name="observer"/> receive the stages of this flow of execution until the result is disposed;
    /// the observer before it comes back then.
    /// </summary>
    public static IDisposable Observe(IrBodyObserver observer)
    {
        var scope = new ObserverScope(s_current.Value);
        s_current.Value = observer;
        return scope;
    }

    /// <summary>
    /// Measures <paramref name="stage"/> until the result is disposed; costs nothing without an observer.
    /// </summary>
    public static IrBodyStageTimer Measure(IrBodyStage stage) => new(s_current.Value, stage);

    private sealed class ObserverScope(IrBodyObserver? previous) : IDisposable
    {
        public void Dispose() => s_current.Value = previous;
    }
}
