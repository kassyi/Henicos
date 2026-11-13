using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal sealed class TakeStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex) =>
        context.Emitter.Writer.AppendLine($"int {StagePrefix}{stageIndex}{CountSuffix} = 0;");

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex,
        ref int openBraces)
    {
        // The count was evaluated once, before the loop, and the stop is checked at the head of the loop body
        // (StackLinqFusionRule): an element that gets here is always given
        context.Emitter.Writer.AppendLine($"{StagePrefix}{stageIndex}{CountSuffix}++;");
    }
}
