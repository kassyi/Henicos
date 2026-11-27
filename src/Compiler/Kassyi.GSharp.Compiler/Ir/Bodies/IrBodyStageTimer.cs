using System.Diagnostics;

namespace Kassyi.GSharp.Compiler.Ir.Bodies;

/// <summary>
/// Measures one stage of the IR body pipeline for the observer that was current when it started; does nothing without
/// one.
/// </summary>
internal readonly struct IrBodyStageTimer : IDisposable
{
    private readonly IrBodyObserver? _observer;
    private readonly IrBodyStage _stage;
    private readonly long _start;

    /// <summary>Starts measuring <paramref name="stage"/> for <paramref name="observer"/>.</summary>
    public IrBodyStageTimer(IrBodyObserver? observer, IrBodyStage stage)
    {
        _observer = observer;
        _stage = stage;
        _start = observer is null ? 0 : Stopwatch.GetTimestamp();
    }

    /// <summary>Reports the time of the stage to the observer.</summary>
    public void Dispose() => _observer?.OnStage(_stage, Stopwatch.GetTimestamp() - _start);
}
