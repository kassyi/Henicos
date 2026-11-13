using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal sealed class AnyStrategy : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage) =>
        context.Emitter.Writer.AppendLine($"bool {resultVar} = false;");

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        if (StackLinqHelper.HasPredicateOrSelector(stage.Invocation, stage.Symbol))
        {
            var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
            context.Emitter.Writer.Append("if ((");
            context.Visitor.Visit(arg.Expression);
            context.Emitter.Writer.AppendLine($")({ValuePrefix}{valIndex}))");
            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.AppendLine($"{resultVar} = true; break;");
            }
        }
        else
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = true; break;");
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) { }
}
