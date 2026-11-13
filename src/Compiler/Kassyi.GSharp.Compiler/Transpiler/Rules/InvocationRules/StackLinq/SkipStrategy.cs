using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal sealed class SkipStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex) =>
        context.Emitter.Writer.AppendLine($"int {StagePrefix}{stageIndex}{CountSuffix} = 0;");

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex,
        ref int openBraces)
    {
        // The count was evaluated once, before the loop (StackLinqFusionRule)
        context.Emitter.Writer.AppendLine(
            $"if ({StagePrefix}{stageIndex}{CountSuffix} < {StagePrefix}{stageIndex}{StageCountSuffix})");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{StagePrefix}{stageIndex}{CountSuffix}++;");
            context.Emitter.Writer.AppendLine("continue;");
        }
    }
}
