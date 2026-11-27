namespace Kassyi.GSharp.Compiler.Ir.Bodies;

/// <summary>Receives the time of each stage of the IR body pipeline, for a benchmark.</summary>
internal abstract class IrBodyObserver
{
    /// <summary>Called when <paramref name="stage"/> ends, with its time in <see cref="System.Diagnostics.Stopwatch"/>
    /// ticks.</summary>
    public abstract void OnStage(IrBodyStage stage, long ticks);
}
