using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

// TakeLast and SkipLast exist only right after an array or span source, so StackLinqFusionRule always folds them into the
// range of the loop (StackLinqChainExtractor.CountRangeStages); nothing is written inside the loop.
internal sealed class TakeLastStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex)
    {
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex, ref int openBraces)
    {
    }
}
