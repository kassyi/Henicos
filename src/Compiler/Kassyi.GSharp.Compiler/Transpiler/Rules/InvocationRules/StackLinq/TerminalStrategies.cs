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
/// Terminal strategy for <c>Max</c> and <c>Min</c>.
/// </summary>
/// <remarks>
/// A <c>float</c> or <c>double</c> sequence places NaN as System.Linq does, whatever its position: <c>Max</c> skips NaN and
/// gives NaN only when every element is NaN; <c>Min</c> gives the first NaN and reads no element after it.
/// </remarks>
internal sealed class ExtremaStrategy(bool isMax) : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        context.Emitter.Writer.AppendLine($" {resultVar} = {{}};");
        context.Emitter.Writer.AppendLine($"bool {resultVar}_init = false;");
    }

    // Max: if (!<res>_init[ || <is_nan>(<res>)]) { <res> = <v>; <res>_init = true; } else { if (<v> > <res>) { <res> = <v>; } }
    // Min: if (!<res>_init) { <res> = <v>; <res>_init = true;[ if (<is_nan>(<res>)) { break; }] }
    //      else { if (<v> < <res>) { <res> = <v>; }[ else if (<is_nan>(<v>)) { <res> = <v>; break; }] }
    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        var isFloating = stage.Symbol.ReturnType.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
        var value = $"{StackLinqHelper.ValuePrefix}{valIndex}";
        var isNaN = GSharpInternalNames.StackLinq.LinqIsNaN;
        var writer = context.Emitter.Writer;

        if (isMax && isFloating)
        {
            writer.AppendLine($"if (!{resultVar}_init || {isNaN}({resultVar}))");
        }
        else
        {
            writer.AppendLine($"if (!{resultVar}_init)");
        }
        using (writer.Braces())
        {
            writer.AppendLine($"{resultVar} = {value};");
            writer.AppendLine($"{resultVar}_init = true;");
            if (!isMax && isFloating)
            {
                writer.AppendLine($"if ({isNaN}({resultVar})) {{ break; }}");
            }
        }
        writer.AppendLine("else");
        using (writer.Braces())
        {
            var comparison = isMax ? ">" : "<";
            writer.AppendLine($"if ({value} {comparison} {resultVar})");
            using (writer.Braces())
            {
                writer.AppendLine($"{resultVar} = {value};");
            }
            if (!isMax && isFloating)
            {
                writer.AppendLine($"else if ({isNaN}({value})) {{ {resultVar} = {value}; break; }}");
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
        if (!StackLinqHelper.TryGetStageArgument(stage.Invocation, stage.Symbol, 0, out var targetArg))
        {
            throw new InvalidOperationException("Target value argument was not found.");
        }

        var targetType = stage.Symbol.Parameters[^1].Type;
        context.Emitter.Dialect.AppendType(ref context, targetType);
        context.Emitter.Writer.Append($" {resultVar}_target = ");
        context.Visitor.Visit(targetArg.Expression);
        context.Emitter.Writer.AppendLine(";");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        context.Emitter.Writer.AppendLine(
            $"if ({GSharpInternalNames.StackLinq.LinqEquals}({StackLinqHelper.ValuePrefix}{valIndex}, {resultVar}_target))");
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
        context.Emitter.Writer.AppendLine($"double {resultVar} = 0;");
        context.Emitter.Writer.AppendLine($"int {resultVar}_count = 0;");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        StackLinqHelper.EmitAccumulateElement(ref context, in stage, resultVar, valIndex);
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
            context.Emitter.Writer.AppendLine($"{resultVar} = {resultVar} / (double)({resultVar}_count);");
        }
    }
}

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
            context.Emitter.Writer.AppendLine($"bool {resultVar}_init = false;");
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

        var funcArg = funcArgSyntax.Expression;

        if (!hasSeed)
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
        if (StackLinqHelper.GetStageArgumentCount(stage.Symbol) < 2)
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
