using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal sealed class ContainsStrategy : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Writer.AppendLine($"bool {resultVar} = false;");
        if (!StackLinqHelper.TryGetStageArgument(stage.Invocation, stage.Symbol, 0, out var targetArg))
        {
            throw new InvalidOperationException("Target value argument was not found.");
        }

        var targetType = stage.Symbol.Parameters[^1].Type;
        context.Emitter.Dialect.AppendType(ref context, targetType);
        context.Emitter.Writer.Append($" {resultVar}{TargetSuffix} = ");
        context.Visitor.Visit(targetArg.Expression);
        context.Emitter.Writer.AppendLine(";");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        context.Emitter.Writer.AppendLine(
            $"if ({LinqEquals}({ValuePrefix}{valIndex}, {resultVar}{TargetSuffix}))");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = true; break;");
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) { }
}
