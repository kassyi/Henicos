namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

/// <summary>
/// Terminal strategy for <c>Sum</c>. A <c>float</c> sum adds in a <c>double</c> accumulator, as System.Linq does; the
/// fused loop rounds it to <c>float</c> once at the end (<see cref="AccumulatesInDouble"/>).
/// </summary>
internal sealed class SumStrategy : IStackLinqTerminalStrategy
{
    public static bool AccumulatesInDouble(in LinqStage stage) =>
        stage.Symbol.ReturnType.SpecialType == SpecialType.System_Single;

    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        if (AccumulatesInDouble(in stage))
        {
            context.Emitter.Writer.Append("double");
        }
        else
        {
            context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        }

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
            context.Emitter.Writer.AppendLine($")({GSharpInternalNames.StackLinq.ValuePrefix}{valIndex}))");
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
            context.Emitter.Writer.AppendLine($")({GSharpInternalNames.StackLinq.ValuePrefix}{valIndex}))");
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
        context.Emitter.Writer.AppendLine($")({GSharpInternalNames.StackLinq.ValuePrefix}{valIndex})))");
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
    private const string ValuePrefix = GSharpInternalNames.StackLinq.ValuePrefix;
    private const string FoundSuffix = GSharpInternalNames.StackLinq.FoundSuffix;

    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        context.Emitter.Writer.AppendLine($" {resultVar} = {{}};");
        if (trapOnEmpty)
        {
            context.Emitter.Writer.AppendLine($"bool {resultVar}{FoundSuffix} = false;");
        }
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        var foundSuffix = trapOnEmpty ? $" {resultVar}{FoundSuffix} = true;" : "";
        var statementSuffix = stopOnFirstMatch ? $";{foundSuffix} break;" : $";{foundSuffix}";
        if (StackLinqHelper.HasPredicateOrSelector(stage.Invocation, stage.Symbol))
        {
            var arg = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol);
            context.Emitter.Writer.Append("if ((");
            context.Visitor.Visit(arg.Expression);
            context.Emitter.Writer.AppendLine($")({ValuePrefix}{valIndex}))");
            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.AppendLine($"{resultVar} = {ValuePrefix}{valIndex}{statementSuffix}");
            }
        }
        else
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = {ValuePrefix}{valIndex}{statementSuffix}");
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
        TerminalTrap.EmitIfUnset(ref context, resultVar, FoundSuffix, message);
    }
}

/// <summary>
/// Terminal strategy for <c>Max</c> and <c>Min</c>.
/// </summary>
/// <remarks>
/// A <c>float</c> or <c>double</c> sequence places NaN as System.Linq does, whatever its position: <c>Max</c> skips
/// NaN and gives NaN only when every element is NaN; <c>Min</c> gives the first NaN and reads no element after it.
/// </remarks>
internal sealed class ExtremaStrategy(bool isMax) : IStackLinqTerminalStrategy
{
    private const string ValuePrefix = GSharpInternalNames.StackLinq.ValuePrefix;
    private const string InitSuffix = GSharpInternalNames.StackLinq.InitSuffix;

    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        context.Emitter.Writer.AppendLine($" {resultVar} = {{}};");
        context.Emitter.Writer.AppendLine($"bool {resultVar}{InitSuffix} = false;");
    }

    // Max: if (!<res>_init[ || <is_nan>(<res>)]) { <res> = <v>; <res>_init = true; }
    //      else { if (<v> > <res>) { <res> = <v>; } }
    // Min: if (!<res>_init) { <res> = <v>; <res>_init = true;[ if (<is_nan>(<res>)) { break; }] }
    //      else { if (<v> < <res>) { <res> = <v>; }[ else if (<is_nan>(<v>)) { <res> = <v>; break; }] }
    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        var isFloating = stage.Symbol.ReturnType.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
        var value = $"{ValuePrefix}{valIndex}";
        var isNaN = GSharpInternalNames.StackLinq.LinqIsNaN;
        var writer = context.Emitter.Writer;

        if (isMax && isFloating)
        {
            writer.AppendLine($"if (!{resultVar}{InitSuffix} || {isNaN}({resultVar}))");
        }
        else
        {
            writer.AppendLine($"if (!{resultVar}{InitSuffix})");
        }
        using (writer.Braces())
        {
            writer.AppendLine($"{resultVar} = {value};");
            writer.AppendLine($"{resultVar}{InitSuffix} = true;");
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
        TerminalTrap.EmitIfUnset(ref context, resultVar, InitSuffix);
}

internal sealed class ContainsStrategy : IStackLinqTerminalStrategy
{
    private const string ValuePrefix = GSharpInternalNames.StackLinq.ValuePrefix;
    private const string TargetSuffix = GSharpInternalNames.StackLinq.TargetSuffix;

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
            $"if ({GSharpInternalNames.StackLinq.LinqEquals}({ValuePrefix}{valIndex}, {resultVar}{TargetSuffix}))");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = true; break;");
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) { }
}

internal sealed class AverageStrategy : IStackLinqTerminalStrategy
{
    private const string CountSuffix = GSharpInternalNames.StackLinq.CountSuffix;

    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Writer.AppendLine($"double {resultVar} = 0;");
        context.Emitter.Writer.AppendLine($"int {resultVar}{CountSuffix} = 0;");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        StackLinqHelper.EmitAccumulateElement(ref context, in stage, resultVar, valIndex);
        context.Emitter.Writer.AppendLine($"{resultVar}{CountSuffix}++;");
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar)
    {
        context.Emitter.Writer.AppendLine($"if ({resultVar}{CountSuffix} == 0)");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine(
                $"{GSharpInternalNames.Prelude.Trap}(" +
                "GSharpErrorCode::InvalidOperation, \"Sequence contains no elements\");");
        }
        context.Emitter.Writer.AppendLine("else");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = {resultVar} / (double)({resultVar}{CountSuffix});");
        }
    }
}

internal sealed class AggregateStrategy : IStackLinqTerminalStrategy
{
    private const string ValuePrefix = GSharpInternalNames.StackLinq.ValuePrefix;
    private const string InitSuffix = GSharpInternalNames.StackLinq.InitSuffix;

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

        var funcArg = funcArgSyntax.Expression;

        if (!hasSeed)
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
                context.Emitter.Writer.Append($"{resultVar} = (");
                context.Visitor.Visit(funcArg);
                context.Emitter.Writer.AppendLine($")({resultVar}, {ValuePrefix}{valIndex});");
            }
        }
        else
        {
            context.Emitter.Writer.Append($"{resultVar} = (");
            context.Visitor.Visit(funcArg);
            context.Emitter.Writer.AppendLine($")({resultVar}, {ValuePrefix}{valIndex});");
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar)
    {
        if (StackLinqHelper.GetStageArgumentCount(stage.Symbol) < 2)
        {
            TerminalTrap.EmitIfUnset(ref context, resultVar, InitSuffix);
        }
    }
}

file static class TerminalTrap
{
    public static void EmitIfUnset(
        ref TranspileContext context,
        string resultVar,
        string flagSuffix,
        string message = "Sequence contains no elements")
    {
        context.Emitter.Writer.AppendLine($"if (!{resultVar}{flagSuffix})");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine(
                $"{GSharpInternalNames.Prelude.Trap}(GSharpErrorCode::InvalidOperation, \"{message}\");");
        }
    }
}
