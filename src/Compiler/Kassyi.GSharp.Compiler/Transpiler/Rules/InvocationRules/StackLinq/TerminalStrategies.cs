namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal sealed class SumStrategy : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        context.Emitter.Writer.AppendLine($" {resultVar} = 0;");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex) =>
        StackLinqHelper.EmitAccumulateElement(ref context, in stage, resultVar, valIndex);

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) { }
}

internal sealed class CountStrategy : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage) =>
        context.Emitter.Writer.AppendLine($"int {resultVar} = 0;");

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        if (StackLinqHelper.HasPredicateOrSelector(stage.Invocation, stage.Symbol))
        {
            var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
            context.Emitter.Writer.Append("if ((");
            context.Visitor.Visit(arg.Expression);
            context.Emitter.Writer.AppendLine($")({StackLinqHelper.ValuePrefix}{valIndex}))");
            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.AppendLine($"{resultVar}++;");
            }
        }
        else
        {
            context.Emitter.Writer.AppendLine($"{resultVar}++;");
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) { }
}

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
            context.Emitter.Writer.AppendLine($")({StackLinqHelper.ValuePrefix}{valIndex}))");
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

internal sealed class AllStrategy : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage) =>
        context.Emitter.Writer.AppendLine($"bool {resultVar} = true;");

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
        context.Emitter.Writer.Append("if (!((");
        context.Visitor.Visit(arg.Expression);
        context.Emitter.Writer.AppendLine($")({StackLinqHelper.ValuePrefix}{valIndex})))");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = false; break;");
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) { }
}

/// <summary>
/// Parameterized terminal strategy for <c>First</c>, <c>FirstOrDefault</c>, <c>Last</c>, and <c>LastOrDefault</c>.
/// </summary>
internal sealed class ElementSelectionStrategy(bool stopOnFirstMatch, bool trapOnEmpty) : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        context.Emitter.Writer.AppendLine($" {resultVar} = {{}};");
        if (trapOnEmpty)
        {
            context.Emitter.Writer.AppendLine($"bool {resultVar}_found = false;");
        }
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        var foundSuffix = trapOnEmpty ? $" {resultVar}_found = true;" : "";
        var statementSuffix = stopOnFirstMatch ? $";{foundSuffix} break;" : $";{foundSuffix}";
        if (StackLinqHelper.HasPredicateOrSelector(stage.Invocation, stage.Symbol))
        {
            var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
            context.Emitter.Writer.Append("if ((");
            context.Visitor.Visit(arg.Expression);
            context.Emitter.Writer.AppendLine($")({StackLinqHelper.ValuePrefix}{valIndex}))");
            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.AppendLine($"{resultVar} = {StackLinqHelper.ValuePrefix}{valIndex}{statementSuffix}");
            }
        }
        else
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = {StackLinqHelper.ValuePrefix}{valIndex}{statementSuffix}");
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar)
    {
        if (!trapOnEmpty)
        {
            return;
        }

        var message = StackLinqHelper.HasPredicateOrSelector(stage.Invocation, stage.Symbol)
            ? "Sequence contains no matching element"
            : "Sequence contains no elements";
        TerminalNames.EmitEmptySequenceTrap(ref context, $"!{resultVar}_found", message);
    }
}

/// <summary>
/// Parameterized terminal strategy for <c>Max</c> and <c>Min</c>.
/// </summary>
internal sealed class ExtremaStrategy(string comparisonOperator) : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        context.Emitter.Writer.AppendLine($" {resultVar} = {{}};");
        context.Emitter.Writer.AppendLine($"bool {resultVar}_init = false;");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        context.Emitter.Writer.AppendLine($"if (!{resultVar}_init)");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = {StackLinqHelper.ValuePrefix}{valIndex};");
            context.Emitter.Writer.AppendLine($"{resultVar}_init = true;");
        }
        context.Emitter.Writer.AppendLine("else");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"if ({StackLinqHelper.ValuePrefix}{valIndex} {comparisonOperator} {resultVar})");
            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.AppendLine($"{resultVar} = {StackLinqHelper.ValuePrefix}{valIndex};");
            }
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) =>
        TerminalNames.EmitEmptySequenceTrap(ref context, $"!{resultVar}_init");
}

internal sealed class ContainsStrategy : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Writer.AppendLine($"bool {resultVar} = false;");
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.Parameters[^1].Type);
        context.Emitter.Writer.Append($" {resultVar}_target = ");
        var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
        context.Visitor.Visit(arg.Expression);
        context.Emitter.Writer.AppendLine(";");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        context.Emitter.Writer.AppendLine(
            $"if (Kassyi::ZeroAlloc::Linq::StackLinq::Equals({StackLinqHelper.ValuePrefix}{valIndex}, {resultVar}_target))");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = true; break;");
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) { }
}

internal sealed class AverageStrategy : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        context.Emitter.Writer.AppendLine($" {resultVar} = 0;");
        context.Emitter.Writer.AppendLine($"double {resultVar}_sum = 0;");
        context.Emitter.Writer.AppendLine($"int {resultVar}_count = 0;");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        context.Emitter.Writer.AppendLine($"{resultVar}_sum += (double){StackLinqHelper.ValuePrefix}{valIndex};");
        context.Emitter.Writer.AppendLine($"{resultVar}_count++;");
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar)
    {
        context.Emitter.Writer.AppendLine($"if ({resultVar}_count == 0)");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{GSharpInternalNames.Prelude.Trap}(GSharpErrorCode::InvalidOperation, \"Sequence contains no elements\");");
        }
        context.Emitter.Writer.AppendLine("else");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.Append($"{resultVar} = (");
            context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
            context.Emitter.Writer.AppendLine($")({resultVar}_sum / (double){resultVar}_count);");
        }
    }
}

internal sealed class AggregateStrategy : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        if (stage.Invocation.ArgumentList.Arguments.Count >= 2)
        {
            context.Emitter.Writer.Append($" {resultVar} = ");
            context.Visitor.Visit(stage.Invocation.ArgumentList.Arguments[0].Expression);
            context.Emitter.Writer.AppendLine(";");
        }
        else
        {
            context.Emitter.Writer.AppendLine($" {resultVar} = {{}};");
            context.Emitter.Writer.AppendLine($"bool {resultVar}_init = false;");
        }
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        var funcArg = stage.Invocation.ArgumentList.Arguments.Count >= 2
            ? stage.Invocation.ArgumentList.Arguments[1].Expression
            : stage.Invocation.ArgumentList.Arguments[0].Expression;

        if (stage.Invocation.ArgumentList.Arguments.Count < 2)
        {
            context.Emitter.Writer.AppendLine($"if (!{resultVar}_init)");
            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.AppendLine($"{resultVar} = {StackLinqHelper.ValuePrefix}{valIndex};");
                context.Emitter.Writer.AppendLine($"{resultVar}_init = true;");
            }
            context.Emitter.Writer.AppendLine("else");
            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.Append($"{resultVar} = (");
                context.Visitor.Visit(funcArg);
                context.Emitter.Writer.AppendLine($")({resultVar}, {StackLinqHelper.ValuePrefix}{valIndex});");
            }
        }
        else
        {
            context.Emitter.Writer.Append($"{resultVar} = (");
            context.Visitor.Visit(funcArg);
            context.Emitter.Writer.AppendLine($")({resultVar}, {StackLinqHelper.ValuePrefix}{valIndex});");
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar)
    {
        if (stage.Invocation.ArgumentList.Arguments.Count < 2)
        {
            TerminalNames.EmitEmptySequenceTrap(ref context, $"!{resultVar}_init");
        }
    }
}

file static class TerminalNames
{
    /// <summary>Prefix of the terminal state flags and counters derived from the fusion result variable.</summary>
    internal const string StatePrefix = "__gsharp_fusion_res_";

    public static void EmitEmptySequenceTrap(
        ref TranspileContext context,
        string condition,
        string message = "Sequence contains no elements")
    {
        context.Emitter.Writer.AppendLine($"if ({condition})");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{GSharpInternalNames.Prelude.Trap}(GSharpErrorCode::InvalidOperation, \"{message}\");");
        }
    }
}
