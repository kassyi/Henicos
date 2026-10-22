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
        context.Emitter.Writer.AppendLine($")(__gsharp_val_{valIndex}))");
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
        context.Emitter.Writer.Append($"auto __gsharp_val_{valIndex} = (");
        context.Visitor.Visit(arg.Expression);
        context.Emitter.Writer.AppendLine($")(__gsharp_val_{valIndex - 1});");
    }
}

internal sealed class TakeStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex) =>
        context.Emitter.Writer.AppendLine($"int __gsharp_stage_{stageIndex}_count = 0;");

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex,
        ref int openBraces)
    {
        var arg = stage.Invocation.ArgumentList.Arguments[0].Expression;
        context.Emitter.Writer.Append($"if (__gsharp_stage_{stageIndex}_count >= ");
        context.Visitor.Visit(arg);
        context.Emitter.Writer.AppendLine(") { break; }");

        context.Emitter.Writer.AppendLine($"__gsharp_stage_{stageIndex}_count++;");
    }
}

internal sealed class SkipStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex) =>
        context.Emitter.Writer.AppendLine($"int __gsharp_stage_{stageIndex}_count = 0;");

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex,
        ref int openBraces)
    {
        var arg = stage.Invocation.ArgumentList.Arguments[0].Expression;
        context.Emitter.Writer.Append($"if (__gsharp_stage_{stageIndex}_count < ");
        context.Visitor.Visit(arg);
        context.Emitter.Writer.AppendLine(")");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"__gsharp_stage_{stageIndex}_count++;");
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
        context.Emitter.Writer.AppendLine($")(__gsharp_val_{valIndex})))");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine("break;");
        }
    }
}

internal sealed class SkipWhileStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex) =>
        context.Emitter.Writer.AppendLine($"bool __gsharp_stage_{stageIndex}_skipping = true;");

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex,
        ref int openBraces)
    {
        var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
        context.Emitter.Writer.AppendLine($"if (__gsharp_stage_{stageIndex}_skipping)");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.Append("if ((");
            context.Visitor.Visit(arg.Expression);
            context.Emitter.Writer.AppendLine($")(__gsharp_val_{valIndex}))");
            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.AppendLine("continue;");
            }

            context.Emitter.Writer.AppendLine($"__gsharp_stage_{stageIndex}_skipping = false;");
        }
    }
}
internal sealed class TakeLastStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex)
    {
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex, ref int openBraces)
    {
        var arg = stage.Invocation.ArgumentList.Arguments[0].Expression;
        context.Emitter.Writer.Append("if (__gsharp_i < __gsharp_fusion_length - (");
        context.Visitor.Visit(arg);
        context.Emitter.Writer.AppendLine("))");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine("continue;");
        }
    }
}

internal sealed class SkipLastStrategy : IStackLinqIntermediateStrategy
{
    public void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex)
    {
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex, ref int openBraces)
    {
        var arg = stage.Invocation.ArgumentList.Arguments[0].Expression;
        context.Emitter.Writer.Append("if (__gsharp_i >= __gsharp_fusion_length - (");
        context.Visitor.Visit(arg);
        context.Emitter.Writer.AppendLine("))");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine("break;");
        }
    }
}
