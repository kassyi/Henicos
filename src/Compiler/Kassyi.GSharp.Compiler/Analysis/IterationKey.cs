namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>One enclosing loop iteration key attached to a <c>Warp.ActiveMask()</c> call.</summary>
internal readonly record struct IterationKey(
    IterationKeyKind Kind,
    int CounterIndex,
    ILocalSymbol? LoopVariable)
{
    public static IterationKey ForCounter(int counterIndex) =>
        new(IterationKeyKind.Counter, counterIndex, null);

    public static IterationKey ForLoopVariable(ILocalSymbol loopVariable) =>
        new(IterationKeyKind.LoopVariable, -1, loopVariable);
}
