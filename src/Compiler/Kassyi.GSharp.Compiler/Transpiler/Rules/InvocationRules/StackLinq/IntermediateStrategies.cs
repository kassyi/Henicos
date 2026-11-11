using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal sealed class WhereStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex)
    {
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex,
        ref int openBraces)
    {
        var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
        context.Emitter.Writer.Append("if ((");
        context.Visitor.Visit(arg.Expression);
        context.Emitter.Writer.AppendLine($")({ValuePrefix}{valIndex}))");
        context.Emitter.Writer.AppendLine("{");
        openBraces++;
    }
}

internal sealed class SelectStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex)
    {
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex,
        ref int openBraces)
    {
        var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
        valIndex++;
        context.Emitter.Writer.Append($"auto {ValuePrefix}{valIndex} = (");
        context.Visitor.Visit(arg.Expression);
        context.Emitter.Writer.AppendLine($")({ValuePrefix}{valIndex - 1});");
    }
}

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

internal sealed class SkipWhileStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex) =>
        context.Emitter.Writer.AppendLine($"bool {StagePrefix}{stageIndex}{StageSkippingSuffix} = true;");

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex,
        ref int openBraces)
    {
        var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
        context.Emitter.Writer.AppendLine($"if ({StagePrefix}{stageIndex}{StageSkippingSuffix})");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.Append("if ((");
            context.Visitor.Visit(arg.Expression);
            context.Emitter.Writer.AppendLine($")({ValuePrefix}{valIndex}))");
            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.AppendLine("continue;");
            }

            context.Emitter.Writer.AppendLine($"{StagePrefix}{stageIndex}{StageSkippingSuffix} = false;");
        }
    }
}
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

internal sealed class SkipLastStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex)
    {
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex, ref int openBraces)
    {
    }
}
