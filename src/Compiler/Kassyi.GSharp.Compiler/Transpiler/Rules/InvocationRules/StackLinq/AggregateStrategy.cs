using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal sealed class AggregateStrategy : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        if (StackLinqHelper.GetStageArgumentCount(stage.Symbol) >= 2 &&
            StackLinqHelper.TryGetStageArgument(stage.Invocation, stage.Symbol, 0, out var seedArg))
        {
            context.Emitter.Writer.Append($" {resultVar} = ");
            context.Visitor.Visit(seedArg.Expression);
            context.Emitter.Writer.AppendLine(";");
        }
        else
        {
            context.Emitter.Writer.AppendLine($" {resultVar} = {{}};");
            context.Emitter.Writer.AppendLine($"bool {resultVar}{InitSuffix} = false;");
        }
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        var hasSeed = StackLinqHelper.GetStageArgumentCount(stage.Symbol) >= 2;
        var funcOrdinal = hasSeed ? 1 : 0;
        if (!StackLinqHelper.TryGetStageArgument(stage.Invocation, stage.Symbol, funcOrdinal, out var funcArgSyntax))
        {
            throw new InvalidOperationException("Function argument was not found.");
        }

        if (hasSeed)
        {
            EmitFold(ref context, funcArgSyntax.Expression, resultVar, valIndex);
        }
        else
        {
            EmitUnseeded(ref context, funcArgSyntax.Expression, resultVar, valIndex);
        }
    }

    // if (!<res>_init) { <res> = <v>; <res>_init = true; } else { <fold> }
    private static void EmitUnseeded(
        ref TranspileContext context,
        ExpressionSyntax funcArg,
        string resultVar,
        int valIndex)
    {
        context.Emitter.Writer.AppendLine($"if (!{resultVar}{InitSuffix})");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = {ValuePrefix}{valIndex};");
            context.Emitter.Writer.AppendLine($"{resultVar}{InitSuffix} = true;");
        }
        context.Emitter.Writer.AppendLine("else");
        using (context.Emitter.Writer.Braces())
        {
            EmitFold(ref context, funcArg, resultVar, valIndex);
        }
    }

    // <res> = (<func>)(<res>, <v>);
    private static void EmitFold(ref TranspileContext context, ExpressionSyntax funcArg, string resultVar, int valIndex)
    {
        context.Emitter.Writer.Append($"{resultVar} = (");
        context.Visitor.Visit(funcArg);
        context.Emitter.Writer.AppendLine($")({resultVar}, {ValuePrefix}{valIndex});");
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar)
    {
        if (StackLinqHelper.GetStageArgumentCount(stage.Symbol) < 2)
        {
            TerminalTrap.EmitIfUnset(ref context, resultVar, InitSuffix);
        }
    }
}
