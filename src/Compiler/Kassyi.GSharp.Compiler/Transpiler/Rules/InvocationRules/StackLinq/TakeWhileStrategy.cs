using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal sealed class TakeWhileStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex)
    {
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex,
        ref int openBraces)
    {
        var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
        context.Emitter.Writer.Append("if (!((");
        context.Visitor.Visit(arg.Expression);
        context.Emitter.Writer.AppendLine($")({ValuePrefix}{valIndex})))");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine("break;");
        }
    }
}
