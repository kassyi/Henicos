namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The public call that evaluates a signed expression (<see cref="GpuBigIntExpr.Evaluate(GpuWorkspace, string, string, int)"/>,
/// <see cref="GpuBigIntExpr.CompileToGraph"/>, ...): the entry point, its caller and the root of the expression. Set for the
/// current thread while the call runs, so that a checked operation can record where it was evaluated.
/// </summary>
internal sealed class EvaluationSite(
    string entryPoint,
    IGpuBigIntExpression root,
    string callerMemberName,
    string callerFilePath,
    int callerLineNumber)
{
    [ThreadStatic] private static EvaluationSite? t_current;

    private List<OverflowSite>? _captured;

    /// <summary>Gets the evaluation running on the current thread, or <see langword="null"/> outside one.</summary>
    public static EvaluationSite? Current => t_current;

    public string EntryPoint { get; } = entryPoint;

    public IGpuBigIntExpression Root { get; } = root;

    public string CallerMemberName { get; } = callerMemberName;

    public string CallerFilePath { get; } = callerFilePath;

    public int CallerLineNumber { get; } = callerLineNumber;

    /// <summary>Makes this the current evaluation until the returned scope is disposed.</summary>
    /// <returns>The scope that restores the previous evaluation.</returns>
    public Scope Enter()
    {
        var previous = t_current;
        t_current = this;
        return new Scope(previous);
    }

    /// <summary>Starts collecting the operations registered while a CUDA graph is captured.</summary>
    public void BeginCapture() => _captured = [];

    /// <summary>Stops collecting and returns the operations registered since <see cref="BeginCapture"/>.</summary>
    /// <returns>The operations whose ids the captured graph replays.</returns>
    public IReadOnlyList<OverflowSite> EndCapture()
    {
        var captured = _captured ?? [];
        _captured = null;
        return captured;
    }

    /// <summary>Adds an operation registered during a capture; outside a capture nothing is kept.</summary>
    /// <param name="site">The registered operation.</param>
    public void RecordCaptured(OverflowSite site) => _captured?.Add(site);

    /// <summary>Restores the evaluation that was current before <see cref="Enter"/>.</summary>
    public readonly struct Scope(EvaluationSite? previous) : IDisposable
    {
        public void Dispose() => t_current = previous;
    }
}
